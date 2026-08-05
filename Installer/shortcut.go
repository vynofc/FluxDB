package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"

	tea "github.com/charmbracelet/bubbletea"
)

func createShortcutsCmd(installDir string) tea.Cmd {
	return func() tea.Msg {
		exePath := findExe(installDir)
		if exePath == "" {
			return errMsg{err: fmt.Errorf("FluxDB.exe nicht gefunden in %s", installDir)}
		}

		exePath = strings.ReplaceAll(exePath, "/", "\\")
		installDir = strings.ReplaceAll(installDir, "/", "\\")

		psScript := fmt.Sprintf(
			`$WshShell = New-Object -ComObject WScript.Shell

$desktop = [Environment]::GetFolderPath("Desktop")
$shortcut = $WshShell.CreateShortcut("$desktop\FluxDB.lnk")
$shortcut.TargetPath = "%s"
$shortcut.WorkingDirectory = "%s"
$shortcut.Description = "FluxDB - File Manager"
$shortcut.Save()

$startMenu = [Environment]::GetFolderPath("Programs") + "\FluxDB"
if (!(Test-Path $startMenu)) { New-Item -ItemType Directory -Path $startMenu -Force | Out-Null }
$shortcut = $WshShell.CreateShortcut("$startMenu\FluxDB.lnk")
$shortcut.TargetPath = "%s"
$shortcut.WorkingDirectory = "%s"
$shortcut.Description = "FluxDB - File Manager"
$shortcut.Save()
`, exePath, installDir, exePath, installDir)

		tmpFile := filepath.Join(os.TempDir(), "fluxdb_shortcut.ps1")
		if err := os.WriteFile(tmpFile, []byte(psScript), 0644); err != nil {
			return errMsg{err: fmt.Errorf("powershell script schreiben fehlgeschlagen: %w", err)}
		}
		defer os.Remove(tmpFile)

		cmd := exec.Command("powershell", "-ExecutionPolicy", "Bypass", "-NoProfile", "-File", tmpFile)
		output, err := cmd.CombinedOutput()
		if err != nil {
			return errMsg{err: fmt.Errorf("verknuepfung fehlgeschlagen: %w: %s", err, string(output))}
		}

		return shortcutCreatedMsg{}
	}
}

func findExe(dir string) string {
	matches, err := filepath.Glob(filepath.Join(dir, "*.exe"))
	if err == nil && len(matches) > 0 {
		return matches[0]
	}
	matches, err = filepath.Glob(filepath.Join(dir, "*", "*.exe"))
	if err == nil && len(matches) > 0 {
		return matches[0]
	}
	return ""
}