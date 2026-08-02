package main

import (
	"archive/zip"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"

	tea "github.com/charmbracelet/bubbletea"
)

func extractCmd(zipPath, customPath string) tea.Cmd {
	return func() tea.Msg {
		installDir := customPath
		if installDir == "" {
			localAppData := os.Getenv("LOCALAPPDATA")
			if localAppData == "" {
				home, _ := os.UserHomeDir()
				localAppData = filepath.Join(home, "AppData", "Local")
			}
			installDir = filepath.Join(localAppData, "FluxDB")
		}

		reader, err := zip.OpenReader(zipPath)
		if err != nil {
			return errMsg{err: fmt.Errorf("ZIP oeffnen fehlgeschlagen: %w", err)}
		}
		defer reader.Close()

		if err := os.MkdirAll(installDir, 0755); err != nil {
			return errMsg{err: fmt.Errorf("installationsverzeichnis erstellen fehlgeschlagen: %w", err)}
		}

		for _, f := range reader.File {
			if err := extractFile(f, installDir); err != nil {
				return errMsg{err: fmt.Errorf("datei extrahieren fehlgeschlagen (%s): %w", f.Name, err)}
			}
		}

		versionFile := filepath.Join(installDir, "version.txt")
		tag := strings.TrimPrefix(filepath.Base(zipPath), "FluxDB-")
		tag = strings.TrimSuffix(tag, ".zip")
		os.WriteFile(versionFile, []byte(tag), 0644)

		os.Remove(zipPath)

		return extractCompleteMsg{installDir: installDir}
	}
}

func extractFile(f *zip.File, destDir string) error {
	rc, err := f.Open()
	if err != nil {
		return err
	}
	defer rc.Close()

	targetPath := filepath.Join(destDir, f.Name)

	if !strings.HasPrefix(targetPath, filepath.Clean(destDir)+string(os.PathSeparator)) {
		return fmt.Errorf("illegaler dateipfad: %s", targetPath)
	}

	if f.FileInfo().IsDir() {
		return os.MkdirAll(targetPath, f.Mode())
	}

	if err := os.MkdirAll(filepath.Dir(targetPath), 0755); err != nil {
		return err
	}

	// Rename locked files (e.g. running .exe) before overwriting
	if _, statErr := os.Stat(targetPath); statErr == nil {
		oldPath := targetPath + ".old"
		os.Remove(oldPath)
		os.Rename(targetPath, oldPath)
	}

	out, err := os.OpenFile(targetPath, os.O_WRONLY|os.O_CREATE|os.O_TRUNC, f.Mode())
	if err != nil {
		return err
	}
	defer out.Close()

	_, err = io.Copy(out, rc)
	return err
}
