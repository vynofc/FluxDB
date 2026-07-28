package main

import (
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"time"

	tea "github.com/charmbracelet/bubbletea"
)

func downloadWithProgressCmd(tag string) tea.Cmd {
	return func() tea.Msg {
		url := buildDownloadURL(tag)
		tmpDir := os.TempDir()
		zipPath := filepath.Join(tmpDir, fmt.Sprintf("FluxDB-%s.zip", tag))

		client := &http.Client{Timeout: 10 * time.Minute}
		req, err := http.NewRequest("GET", url, nil)
		if err != nil {
			return errMsg{err: fmt.Errorf("download-request erstellen fehlgeschlagen: %w", err)}
		}
		req.Header.Set("User-Agent", "FluxDB-Installer")

		resp, err := client.Do(req)
		if err != nil {
			return errMsg{err: fmt.Errorf("download fehlgeschlagen: %w", err)}
		}
		defer resp.Body.Close()

		if resp.StatusCode != http.StatusOK {
			return errMsg{err: fmt.Errorf("download fehlgeschlagen: HTTP %s", resp.Status)}
		}

		out, err := os.Create(zipPath)
		if err != nil {
			return errMsg{err: fmt.Errorf("temporäre datei erstellen fehlgeschlagen: %w", err)}
		}
		defer out.Close()

		if _, err := io.Copy(out, resp.Body); err != nil {
			os.Remove(zipPath)
			return errMsg{err: fmt.Errorf("download schreiben fehlgeschlagen: %w", err)}
		}

		return downloadCompleteMsg{path: zipPath}
	}
}