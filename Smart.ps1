# NEONDECK v1.1 - Native Windows storage health and reliability counters.
# Read-only! No disk writes, repairs, SMART resets or configuration changes.
# Format consumed by NeonDeck: DISK|WindowsDiskNumber|FriendlyName|Health|WearUsedPct|TempC|Bus
$ErrorActionPreference = 'SilentlyContinue'
try {
    $physical = @(Get-PhysicalDisk -ErrorAction SilentlyContinue)
    $disks = @(Get-Disk -ErrorAction SilentlyContinue)
    foreach ($disk in $disks) {
        $number = [string]$disk.Number
        $name = ([string]$disk.FriendlyName) -replace '[|\r\n]', ' '
        $health = [string]$disk.HealthStatus
        $bus = [string]$disk.BusType
        $rel = $null
        try { $rel = $disk | Get-StorageReliabilityCounter -ErrorAction Stop } catch {}
        if ($null -eq $rel) {
            # Only use a physical-drive fallback if exactly one model/size matches.
            $found = @($physical | Where-Object {
                $_.FriendlyName -eq $disk.FriendlyName -and [uint64]$_.Size -eq [uint64]$disk.Size
            })
            if ($found.Count -eq 1) {
                try { $rel = $found[0] | Get-StorageReliabilityCounter -ErrorAction Stop } catch {}
            }
        }
        $wear = ''
        $temp = ''
        if ($null -ne $rel) {
            # Wear from Windows Storage is percent USED, not percent remaining.
            if ($null -ne $rel.Wear -and "$( $rel.Wear )" -ne '') {
                $wearNumber = 0.0
                if ([double]::TryParse([string]$rel.Wear,
                    [System.Globalization.NumberStyles]::Float,
                    [System.Globalization.CultureInfo]::InvariantCulture,
                    [ref]$wearNumber) -and $wearNumber -ge 0 -and $wearNumber -le 100) {
                    $wear = $wearNumber.ToString('0.##', [System.Globalization.CultureInfo]::InvariantCulture)
                }
            }
            $temperature = 0
            if ([int]::TryParse([string]$rel.Temperature, [ref]$temperature) -and
                $temperature -gt 0 -and $temperature -lt 125) {
                $temp = [string]$temperature
            }
        }
        if ([string]::IsNullOrWhiteSpace($health)) { $health = 'N/A' }
        Write-Output "DISK|$number|$name|$health|$wear|$temp|$bus"
    }
} catch {
    # Absence of Storage cmdlets, permissions, drivers or unsupported disks => N/A in UI.
}
