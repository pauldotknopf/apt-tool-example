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
mkdir rootfs-mounted/boot
mount ${loop_device}p1 rootfs-mounted/boot
cp -a rootfs/* rootfs-mounted

# Install GRUB
arch-chroot rootfs-mounted grub-install ${DEVICE} --target=x86_64-efi --efi-directory=/boot
arch-chroot rootfs-mounted grub-mkconfig -o /boot/grub/grub.cfg

# Set the fstab
echo "UUID=cf35024a-90c3-4ee7-b04a-7594f6ff48a0	/         	ext4      	rw,relatime,data=ordered	0 1" >> rootfs-mounted/etc/fstab
echo "UUID=AB91-8E58      	/boot     	vfat      	rw,relatime,fmask=0022,dmask=0022,codepage=437,iocharset=iso8859-1,shortname=mixed,errors=remount-ro	0 2" >> rootfs-mounted/etc/fstab

# Clean up
umount rootfs-mounted/boot
umount rootfs-mounted
rm -r rootfs-mounted
losetup -d ${loop_device}

qemu-img convert -O vmdk boot.img boot.vmdk