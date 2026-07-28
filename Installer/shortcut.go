package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"

	tea "github.com/charmbracelet/bubbletea"
)

func createShortcutCmd(installDir string) tea.Cmd {
	return func() tea.Msg {
		desktopDir := filepath.Join(os.Getenv("USERPROFILE"), "Desktop")
		shortcutPath := filepath.Join(desktopDir, "FluxDB.lnk")

		exePath := findExe(installDir)
		if exePath == "" {
			return errMsg{err: fmt.Errorf("FluxDB.exe nicht gefunden in %s", installDir)}
		}

		if err := createWindowsShortcut(shortcutPath, exePath, installDir); err != nil {
			return errMsg{err: fmt.Errorf("verknuepfung erstellen fehlgeschlagen: %w", err)}
		}

		return shortcutCreatedMsg{}
	}
}

func findExe(dir string) string {
	matches, _ := filepath.Glob(filepath.Join(dir, "*.exe"))
	if len(matches) > 0 {
		return matches[0]
	}
	matches, _ = filepath.Glob(filepath.Join(dir, "*", "*.exe"))
	if len(matches) > 0 {
		return matches[0]
	}
	return ""
}

func createWindowsShortcut(shortcutPath, targetPath, workingDir string) error {
	shortcutPath = strings.ReplaceAll(shortcutPath, "/", "\\")
	targetPath = strings.ReplaceAll(targetPath, "/", "\\")
	workingDir = strings.ReplaceAll(workingDir, "/", "\\")

	vbsContent := fmt.Sprintf(
		`Set WshShell = WScript.CreateObject("WScript.Shell")
Set Shortcut = WshShell.CreateShortcut("%s")
Shortcut.TargetPath = "%s"
Shortcut.WorkingDirectory = "%s"
Shortcut.Description = "FluxDB - File Manager"
Shortcut.Save
`, shortcutPath, targetPath, workingDir)

	tmpFile := filepath.Join(os.TempDir(), "fluxdb_shortcut.vbs")
	if err := os.WriteFile(tmpFile, []byte(vbsContent), 0644); err != nil {
		return err
	}
	defer os.Remove(tmpFile)

	cmd := exec.Command("cscript", "//Nologo", tmpFile)
	output, err := cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("%w: %s", err, string(output))
	}

	return nil
}