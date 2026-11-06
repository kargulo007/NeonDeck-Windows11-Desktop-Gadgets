@echo off
setlocal
chcp 65001 > nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
 "Write-Host '=== VOLUMES AND NAMES ==='; Get-CimInstance Win32_LogicalDisk ^| Select-Object DeviceID,VolumeName,DriveType,Size,FreeSpace,ProviderName ^| Format-Table -AutoSize; Write-Host '=== PHYSICAL DISKS ==='; Get-CimInstance Win32_DiskDrive ^| Select-Object Index,Model,Size,InterfaceType ^| Format-Table -AutoSize; Write-Host '=== PHYSICAL I/O COUNTERS ==='; Get-CimInstance Win32_PerfFormattedData_PerfDisk_PhysicalDisk ^| Select-Object Name,DiskReadBytesPerSec,DiskWriteBytesPerSec ^| Format-Table -AutoSize; Write-Host '=== LOGICAL I/O COUNTERS ==='; Get-CimInstance Win32_PerfFormattedData_PerfDisk_LogicalDisk ^| Select-Object Name,DiskReadBytesPerSec,DiskWriteBytesPerSec ^| Format-Table -AutoSize"
echo.
echo Press any key to close the diagnostic window.
pause > nul
