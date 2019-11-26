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

            Info($"Configuration: {options.Config}");
            var gitVersion = GetGitVersion(ExpandPath("./"));
            Info($"Version: {gitVersion.FullVersion}");
            
            var commandBuildArgs = $"--configuration {options.Config}";
            var commandBuildArgsWithVersion = commandBuildArgs;
            if (!string.IsNullOrEmpty(gitVersion.PreReleaseTag))
            {
                commandBuildArgsWithVersion += $" --version-suffix \"{gitVersion.PreReleaseTag}\"";
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
                RunShell("sudo apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys AA8E81B4331F7F50 648ACFD622F3D138");
            });
            
            Target("refresh-packages", () =>
            {
                RunShell("cd ./images/boot/ && apt-tool install");
                RunShell("cd ./images/runtime/ && apt-tool install");
            });
            
            Target("generate-rootfs", () =>
            {
                RunShell("cd ./images/boot/ && apt-tool generate-rootfs --overwrite --run-stage2");
                RunShell("cd ./images/runtime/ && apt-tool generate-rootfs --overwrite --run-stage2");
            });
            
            Target("build-boot-artifacts", () =>
            {
                RunShell("rm -rf ./output/boot && mkdir ./output/boot");
                RunShell("cp ./images/boot/rootfs/boot/{initrd*,vmlinuz*} ./output/boot");
                RunShell("arch-chroot ./images/boot/rootfs grub-mkimage " +
                         "-d /usr/lib/grub/x86_64-efi " +
                         "-o bootx64.efi " +
                         "-p /efi/boot " +
                         "-O x86_64-efi " +
                         "fat iso9660 part_gpt part_msdos normal boot linux configfile loopback chain efifwsetup efi_gop efi_uga ls search search_label search_fs_uuid search_fs_file gfxterm gfxterm_background gfxterm_menu test all_video loadenv exfat ext2 ntfs btrfs hfsplus udf");
                RunShell("cp ./images/boot/rootfs/bootx64.efi ./output/boot");
                using (var stream = System.IO.File.OpenWrite("./output/boot/grub-initial.cfg"))
                using (var writer = new StreamWriter(stream))
                {
                    writer.WriteLine("search --label \"rootos\" --set prefix");
                    writer.WriteLine("configfile ($prefix)/boot/grub/grub.cfg");
                }
            });
            
            Target("create-image", () =>
            {
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
                    RunShell("cp ./output/boot/bootx64.efi ./mnt/EFI/BOOT/bootx64.efi");
                    RunShell("cp ./output/boot/grub-initial.cfg ./mnt/EFI/BOOT/grub.cfg");
                }
            });
            
            Target("prepare-os-partition", () =>
            {
                using (var device = new LoopDevice("./output/drive.img"))
                using (new Mount(device.Partition(2), "./mnt"))
                {
                    RunShell("rsync -a ./images/runtime/rootfs/ ./mnt");
                    RunShell("cp ./output/boot/{initrd*,vmlinuz*} ./mnt/boot");
                    RunShell("cp ./resources/fstab ./mnt/etc/fstab");
                    RunShell("mkdir ./mnt/boot/grub && cp ./resources/grub.cfg ./mnt/boot/grub/grub.cfg");
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
                "build-boot-artifacts",
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