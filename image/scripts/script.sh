#!/usr/bin/env bash

# Create the user.
echo -en "root\nroot" | passwd root
adduser --disabled-password --gecos "" demo
echo -en "demo\ndemo" | passwd demo

grub-mkimage \
    -d /usr/lib/grub/x86_64-efi \
    -o /boot/bootx64.efi \
    -p /efi/boot \
    -O x86_64-efi \
    fat iso9660 part_gpt part_msdos normal boot linux configfile loopback chain efifwsetup efi_gop efi_uga ls search search_label search_fs_uuid search_fs_file gfxterm gfxterm_background gfxterm_menu test all_video loadenv exfat ext2 ntfs btrfs hfsplus udf