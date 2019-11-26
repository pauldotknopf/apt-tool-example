using System;
using System.IO;
using Build.Buildary;
using static Build.Buildary.Log;
using static Build.Buildary.GitVersion;
using static Build.Buildary.Path;
using static Build.Buildary.Directory;
using static Build.Buildary.Shell;
using static Build.Buildary.File;
using static Bullseye.Targets;

namespace Build
{
    class Program
    {
        static void Main(string[] args)
        {
            NoEcho = false; // Print all the commands that are being run.
            
            var options = Runner.ParseOptions<Runner.RunnerOptions>(args);

            if (int.Parse(ReadShell("id -u")) != 0)
            {
                Failure("You must run as root.");
                Environment.Exit(1);
            }

            Target("clean", () =>
            {
                CleanDirectory(ExpandPath("./output"));
            });
            
            Target("install-dependencies", () =>
            {
                RunShell("apt-get install -y qemu-kvm ovmf");
            });
            
            Target("install-keys", () =>
            {
                RunShell("apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys AA8E81B4331F7F50 648ACFD622F3D138");
            });
            
            Target("refresh-packages", () =>
            {
                RunShell("cd ./image/ && apt-tool install");
            });
            
            Target("generate-rootfs", () =>
            {
                RunShell("cd ./image/ && apt-tool generate-rootfs --overwrite --run-stage2");
            });
            
            Target("create-image", () =>
            {
                RunShell("mkdir ./output || true");
                RunShell("rm -rf ./output/drive.img");
                RunShell("truncate -s 11500MiB ./output/drive.img");
                RunShell("parted -s ./output/drive.img " +
                         "mklabel gpt " +
                         "mkpart primary 0% 500MiB " +
                         "mkpart primary 500MiB 10500MiB " +
                         "mkpart data 10500MiB 100% " +
                         "set 1 esp on");

                using (var loopDevice = new LoopDevice("./output/drive.img"))
                {
                    RunShell($"mkdosfs -n EFI -i 0xAB918E58 {loopDevice.Partition(1)}");
                    RunShell($"mkfs.ext4 -i 8192 -L rootos -U cf35024a-90c3-4ee7-b04a-7594f6ff48a0 {loopDevice.Partition(2)}");
                    RunShell($"mkfs.ext4 -i 8192 -L data -U c83dfb09-d802-4de8-bf2c-b558552c4bd4 {loopDevice.Partition(3)}");
                }
            });
            
            Target("prepare-boot-partition", () =>
            {
                using (var device = new LoopDevice("./output/drive.img"))
                using (new Mount(device.Partition(1), "./mnt"))
                {
                    RunShell("mkdir -p ./mnt/EFI/BOOT");
                    RunShell("cp ./image/rootfs/boot/bootx64.efi ./mnt/EFI/BOOT/bootx64.efi");
                    RunShell("cp ./resources/grub-initial.cfg ./mnt/EFI/BOOT/grub.cfg");
                }
            });
            
            Target("prepare-os-partition", () =>
            {
                using (var device = new LoopDevice("./output/drive.img"))
                using (new Mount(device.Partition(2), "./mnt"))
                {
                    RunShell("rsync -a ./image/rootfs/ ./mnt");
                    RunShell("cp ./resources/fstab ./mnt/etc/fstab");
                    RunShell("mkdir ./mnt/boot/grub || true && cp ./resources/grub.cfg ./mnt/boot/grub/grub.cfg");
                }
            });
            
            Target("debug-partitions", () =>
            {
                using (var loopDevice = new LoopDevice("./output/drive.img"))
                {
                    RunShell("rm -rf ./mnt && mkdir ./mnt && mkdir ./mnt/boot && mkdir ./mnt/rootfs && mkdir ./mnt/data");
                    using (new Mount(loopDevice.Partition(1), "./mnt/boot"))
                    using (new Mount(loopDevice.Partition(2), "./mnt/rootfs"))
                    using (new Mount(loopDevice.Partition(3), "./mnt/data"))
                    {
                        Info("Press enter to finish...");
                        Console.ReadLine();
                    }
                }
            });
            
            Target("run-image", () =>
            {
                RunShell("kvm --bios /usr/share/qemu/OVMF.fd -drive format=raw,file=./output/drive.img -serial stdio -m 4G -cpu host -smp 2");
            });

            Target("default", DependsOn(
                "clean",
                "install-keys",
                "generate-rootfs",
                "create-image",
                "prepare-boot-partition",
                "prepare-os-partition"));
            
            Runner.Execute(options);
        }

        public class LoopDevice : IDisposable
        {
            public LoopDevice(string image)
            {
                Device = ReadShell($"losetup --partscan --show --find {image}").TrimEnd(Environment.NewLine.ToCharArray());
            }
            
            public string Device { get;}

            public string Partition(int partition)
            {
                Console.WriteLine("device ddd" + Device);
                return $"{Device}p{partition}";
            }
            
            public void Dispose()
            {
                RunShell($"losetup -d {Device}");
            }
        }

        public class Mount : IDisposable
        {
            public Mount(string device, string directory)
            {
                Console.WriteLine("SFSDF");
                Console.WriteLine(directory);

                Directory = directory;
                RunShell($"mkdir {directory} || true");
                RunShell($"mount {device} {directory}");
            }
            
            public string Directory { get; }
            
            public void Dispose()
            {
                RunShell($"umount {Directory}");
            }
        }
    }
}