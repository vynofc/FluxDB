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

func extractCmd(zipPath, customPath, tag string) tea.Cmd {
	return func() tea.Msg {
		installDir := customPath
		if installDir == "" {
			localAppData := os.Getenv("LOCALAPPDATA")
			if localAppData == "" {
				home, err := os.UserHomeDir()
				if err != nil {
					home = os.Getenv("USERPROFILE")
				}
				if home == "" {
					return errMsg{err: fmt.Errorf("installationsverzeichnis konnte nicht ermittelt werden")}
				}
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
		if err := os.WriteFile(versionFile, []byte(tag), 0644); err != nil {
			return errMsg{err: fmt.Errorf("version datei schreiben fehlgeschlagen: %w", err)}
		}

		// Close the ZIP reader before removing the file (Windows file locking)
		reader.Close()

		if err := os.Remove(zipPath); err != nil {
			return errMsg{err: fmt.Errorf("temporaere ZIP entfernen fehlgeschlagen: %w", err)}
		}

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

	cleanDest := filepath.Clean(destDir) + string(os.PathSeparator)
	cleanTarget := filepath.Clean(targetPath) + string(os.PathSeparator)
	if !strings.HasPrefix(cleanTarget, cleanDest) {
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
		if err := os.Rename(targetPath, oldPath); err != nil {
			_ = os.Remove(oldPath)
		}
	}

	out, err := os.OpenFile(targetPath, os.O_WRONLY|os.O_CREATE|os.O_TRUNC, f.Mode())
	if err != nil {
		return err
	}
	defer out.Close()

	_, err = io.Copy(out, rc)
	return err
}
