#!/usr/bin/env bash
set -ex

function test-command() {
    command -v $1 >/dev/null 2>&1 || { echo >&2 "The command \"$1\" is required.  Try \"apt-get install $2\"."; exit 1; }
}

test-command arch-chroot arch-install-scripts
test-command parted parted

# Create our hard disk
rm -rf boot.img
truncate -s 11500MiB boot.img
parted -s boot.img \
        mklabel gpt \
        mkpart primary 0% 500MiB \
        mkpart primary 500MiB 10500MiB \
        mkpart data 10500MiB 100% \
        set 1 esp on

# Mount the newly created drive
loop_device=`losetup --partscan --show --find boot.img`

# Format the partitions
mkdosfs -n EFI -i 0xAB918E58 ${loop_device}p1
mkfs.ext4 -i 8192 -L medxplatform -U cf35024a-90c3-4ee7-b04a-7594f6ff48a0 ${loop_device}p2
mkfs.ext4 -i 8192 -L data -U c83dfb09-d802-4de8-bf2c-b558552c4bd4 ${loop_device}p3

# Mount the new partitions
rm -rf rootfs-mounted && mkdir rootfs-mounted
mount ${loop_device}p2 rootfs-mounted
rsync -a images/runtime/rootfs/ rootfs-mounted/

# Copy over the kernel/initramfs from the boot image.
rsync -a images/boot/rootfs/boot/ rootfs-mounted/boot/

# Build the grub.cfg and efi image.
echo "GRUB_DEVICE=\"${loop_device}p2\"" >> images/boot/rootfs/etc/default/grub
echo "GRUB_DEVICE_UUID=\"cf35024a-90c3-4ee7-b04a-7594f6ff48a0\"" >> images/boot/rootfs/etc/default/grub
arch-chroot images/boot/rootfs grub-mkconfig -o grub.cfg
arch-chroot images/boot/rootfs grub-mkimage \
  -d /usr/lib/grub/x86_64-efi \
  -o bootx64.efi \
  -p /efi/boot \
  -O x86_64-efi \
    fat iso9660 part_gpt part_msdos normal boot linux configfile loopback chain efifwsetup efi_gop \
    efi_uga ls search search_label search_fs_uuid search_fs_file gfxterm gfxterm_background \
    gfxterm_menu test all_video loadenv exfat ext2 ntfs btrfs hfsplus udf
cp images/boot/rootfs/grub.cfg rootfs-mounted/boot/grub/grub.cfg

# Set the fstab
echo "UUID=cf35024a-90c3-4ee7-b04a-7594f6ff48a0	/         	ext4      	rw,relatime,data=ordered	0 1" >> rootfs-mounted/etc/fstab

# Finish with rootfs
umount rootfs-mounted

# Now let's prep the EFI partition
mount ${loop_device}p1 rootfs-mounted
mkdir -p rootfs-mounted/EFI/BOOT
cp images/boot/rootfs/bootx64.efi rootfs-mounted/EFI/BOOT
cat <<GRUBCFG > rootfs-mounted/EFI/BOOT/grub.cfg
search --label "medxplatform" --set prefix
configfile (\$prefix)/boot/grub/grub.cfg
GRUBCFG
umount rootfs-mounted
rm -r rootfs-mounted

losetup -d ${loop_device}

#qemu-img convert -O vmdk boot.img boot.vmdk

#kvm -drive format=raw,file=test.img -serial stdio -m 4G -cpu host -smp 2
#kvm --bios /usr/share/qemu/OVMF.fd -net none -drive format=raw,file=boot.img -serial stdio -m 4G -cpu host -smp 2