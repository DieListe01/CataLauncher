Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem

# === Admin-Prüfung ===
if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell "-ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

# === IP-Adresse ===
$ip = (Get-NetIPAddress -AddressFamily IPv4 |
Where-Object {$_.IPAddress -notlike "127.*"} |
Select-Object -First 1 -ExpandProperty IPAddress)

# === GUI erstellen ===
$form = New-Object System.Windows.Forms.Form
$form.Text = "Catan Starter – made by Dirk _DieListe_ Lindner"
$form.Size = New-Object System.Drawing.Size(650,500)
$form.StartPosition = "CenterScreen"
$form.BackColor = "#2f2b24"
$form.ForeColor = "White"
$form.Font = New-Object System.Drawing.Font("Segoe UI",10)

# Titel
$title = New-Object System.Windows.Forms.Label
$title.Text = "Catan LAN Starter"
$title.Font = New-Object System.Drawing.Font("Segoe UI",16,[System.Drawing.FontStyle]::Bold)
$title.AutoSize = $true
$title.Location = New-Object System.Drawing.Point(200,10)
$form.Controls.Add($title)

# --- IP-Gruppe ---
$groupIP = New-Object System.Windows.Forms.GroupBox
$groupIP.Text = "Deine LAN-Adresse"
$groupIP.Size = New-Object System.Drawing.Size(610,70)
$groupIP.Location = New-Object System.Drawing.Point(20,50)
$form.Controls.Add($groupIP)

$ipBox = New-Object System.Windows.Forms.TextBox
$ipBox.Text = $ip
$ipBox.Size = New-Object System.Drawing.Size(250,25)
$ipBox.Location = New-Object System.Drawing.Point(20,25)
$groupIP.Controls.Add($ipBox)

# Buttons
$copyBtn = New-Object System.Windows.Forms.Button
$copyBtn.Text = "IP kopieren"
$copyBtn.Size = New-Object System.Drawing.Size(120,28)
$copyBtn.Location = New-Object System.Drawing.Point(20,23)
$copyBtn.BackColor = "#c79b3b"
$copyBtn.ForeColor = "Black"
$copyBtn.Font = New-Object System.Drawing.Font("Segoe UI",9,[System.Drawing.FontStyle]::Bold)
$copyBtn.Add_Click({ [System.Windows.Forms.Clipboard]::SetText($ipBox.Text) })
$groupIP.Controls.Add($copyBtn)

$fwResetBtn = New-Object System.Windows.Forms.Button
$fwResetBtn.Text = "Firewall zurücksetzen"
$fwResetBtn.Size = New-Object System.Drawing.Size(180,28)
$fwResetBtn.Location = New-Object System.Drawing.Point(150,23)
$fwResetBtn.BackColor = "#c79b3b"
$fwResetBtn.ForeColor = "Black"
$fwResetBtn.Font = New-Object System.Drawing.Font("Segoe UI",9,[System.Drawing.FontStyle]::Bold)
$fwResetBtn.Add_Click({
    Write-Log "Setze Firewall zurück..."
    Set-NetFirewallProfile -Profile Domain,Public,Private -Default | Out-Null
    Write-Log "Firewall auf Standard zurückgesetzt."
})
$groupIP.Controls.Add($fwResetBtn)

$dgDownloadBtn = New-Object System.Windows.Forms.Button
$dgDownloadBtn.Text = "dgVoodoo Download"
$dgDownloadBtn.Size = New-Object System.Drawing.Size(180,28)
$dgDownloadBtn.Location = New-Object System.Drawing.Point(340,23)
$dgDownloadBtn.BackColor = "#c79b3b"
$dgDownloadBtn.ForeColor = "Black"
$dgDownloadBtn.Font = New-Object System.Drawing.Font("Segoe UI",9,[System.Drawing.FontStyle]::Bold)
$dgDownloadBtn.Add_Click({
    Start-Process "https://dege.fw.hu/dgVoodoo2/dgVoodoo2/"
})
$groupIP.Controls.Add($dgDownloadBtn)

# --- Start-Button ---
$startBtn = New-Object System.Windows.Forms.Button
$startBtn.Text = "Spiel starten"
$startBtn.Font = New-Object System.Drawing.Font("Segoe UI",11,[System.Drawing.FontStyle]::Bold)
$startBtn.BackColor = "#c79b3b"
$startBtn.ForeColor = "Black"
$startBtn.Size = New-Object System.Drawing.Size(200,40)
$startBtn.Location = New-Object System.Drawing.Point(220,130)
$form.Controls.Add($startBtn)

# --- Log-Gruppe ---
$groupLog = New-Object System.Windows.Forms.GroupBox
$groupLog.Text = "Status"
$groupLog.Size = New-Object System.Drawing.Size(610,250)
$groupLog.Location = New-Object System.Drawing.Point(20,180)
$form.Controls.Add($groupLog)

$logBox = New-Object System.Windows.Forms.RichTextBox
$logBox.Multiline = $true
$logBox.ScrollBars = "Vertical"
$logBox.ReadOnly = $true
$logBox.BackColor = "#1e1e1e"
$logBox.ForeColor = "#7CFC00"
$logBox.Font = New-Object System.Drawing.Font("Consolas",9)
$logBox.Size = New-Object System.Drawing.Size(580,210)
$logBox.Location = New-Object System.Drawing.Point(15,25)
$logBox.HideSelection = $false
$groupLog.Controls.Add($logBox)

# --- Logging ---
function Write-Log($text, $type="info") {
    $color = switch ($type) {
        "info" {"#7CFC00"}
        "warn" {"#FFD700"}
        "error" {"#FF4500"}
        default {"#7CFC00"}
    }
    $form.Invoke([action]{
        $logBox.SelectionStart = $logBox.TextLength
        $logBox.SelectionLength = 0
        $logBox.SelectionColor = [System.Drawing.ColorTranslator]::FromHtml($color)
        $logBox.AppendText("$text`r`n")
        $logBox.SelectionColor = $logBox.ForeColor
        $logBox.ScrollToCaret()
    })
}

# --- Start-Button ---
$startBtn.Add_Click({

$startBtn.Enabled = $false
Write-Log "Initialisiere Launcher..."

# --- dgVoodoo prüfen ---
$dgPath = "F:\Spiele\dgVoodoo2_86_2\dgVoodooCpl.exe"
if (-not (Test-Path $dgPath)) {
    Write-Log "dgVoodoo fehlt! Bitte den Button 'dgVoodoo Download' verwenden oder manuell installieren." "warn"
    $startBtn.Enabled = $true
    return
}

# --- Catan prüfen ---
$catanPath = "F:\Spiele\Catan\Catan.exe"
if (-not (Test-Path $catanPath)) {
    Write-Log "Catan.exe nicht gefunden! Prüfe Installation." "error"
    $startBtn.Enabled = $true
    return
}

# --- DirectPlay aktivieren ---
Write-Log "Prüfe DirectPlay..."
try { Enable-WindowsOptionalFeature -Online -FeatureName DirectPlay -NoRestart | Out-Null; Write-Log "DirectPlay aktiviert." } 
catch { Write-Log "DirectPlay konnte nicht aktiviert werden (evtl. schon aktiv)." "warn" }

# --- Firewall-Regeln ---
Write-Log "Setze Firewall-Regeln für Catan..."
try {
    New-NetFirewallRule -DisplayName "Catan DirectPlay TCP" -Direction Inbound -Protocol TCP -LocalPort 47624 -Action Allow -ErrorAction SilentlyContinue | Out-Null
    New-NetFirewallRule -DisplayName "Catan DirectPlay UDP" -Direction Inbound -Protocol UDP -LocalPort 2300-2400 -Action Allow -ErrorAction SilentlyContinue | Out-Null
    Write-Log "Firewall-Regeln gesetzt."
} catch { Write-Log "Fehler beim Setzen der Firewall-Regeln." "error" }

# --- dgVoodoo starten ---
Write-Log "Starte dgVoodoo..."
$dgVoodoo = Start-Process $dgPath -PassThru
Start-Sleep 2

# --- Catan starten ---
Write-Log "Lade Catan..."
$catan = Start-Process $catanPath -WorkingDirectory "F:\Spiele\Catan" -PassThru
Write-Log "Catan läuft..."

Wait-Process -Id $catan.Id

Write-Log "Spiel beendet."
Write-Log "Beende dgVoodoo..."
Stop-Process -Id $dgVoodoo.Id -Force -ErrorAction SilentlyContinue
Write-Log "Launcher fertig."

$startBtn.Enabled = $true
})

$form.ShowDialog()