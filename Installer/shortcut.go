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

		tmpFile, err := os.CreateTemp(os.TempDir(), "fluxdb_shortcut_*.ps1")
		if err != nil {
			return errMsg{err: fmt.Errorf("temporaere datei erstellen fehlgeschlagen: %w", err)}
		}
		tmpPath := tmpFile.Name()
		if _, err := tmpFile.Write([]byte(psScript)); err != nil {
			tmpFile.Close()
			os.Remove(tmpPath)
			return errMsg{err: fmt.Errorf("powershell script schreiben fehlgeschlagen: %w", err)}
		}
		tmpFile.Close()
		defer os.Remove(tmpPath)

		cmd := exec.Command("powershell", "-ExecutionPolicy", "Bypass", "-NoProfile", "-File", tmpPath)
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