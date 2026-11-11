param([Parameter(Mandatory=$true)][string]$CommandFile)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$screens = [System.Windows.Forms.Screen]::AllScreens
$forms = @()
foreach ($index in 0,1) {
    $form = New-Object System.Windows.Forms.Form
    $form.Text = if ($index -eq 0) { 'DockMonitorFixture-A' } else { 'DockMonitorFixture-B' }
    $form.StartPosition = 'Manual'
    $form.Size = New-Object System.Drawing.Size(360,160)
    $form.Location = New-Object System.Drawing.Point(($screens[$index].Bounds.X+180),($screens[$index].Bounds.Y+180))
    $form.Show()
    $forms += $form
}
$script:lastCommand = ''
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 200
$timer.Add_Tick({
    if (Test-Path -LiteralPath $CommandFile) {
        $command = [System.IO.File]::ReadAllText($CommandFile).Trim()
        if ($command -ne $script:lastCommand) {
            $script:lastCommand = $command
            if ($command -eq 'swap') {
                $forms[0].Location = New-Object System.Drawing.Point(($screens[1].Bounds.X+180),($screens[1].Bounds.Y+180))
                $forms[1].Location = New-Object System.Drawing.Point(($screens[0].Bounds.X+180),($screens[0].Bounds.Y+180))
            }
            if ($command -eq 'minimize') { $forms | ForEach-Object { $_.WindowState = 'Minimized' } }
            if ($command -eq 'close') { $timer.Stop(); $forms | ForEach-Object { $_.Close() }; [System.Windows.Forms.Application]::ExitThread() }
        }
    }
})
$timer.Start()
[System.Windows.Forms.Application]::Run()
$timer.Dispose()
$forms | ForEach-Object { $_.Dispose() }
