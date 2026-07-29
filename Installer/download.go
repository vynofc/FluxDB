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

type downloadStartedMsg struct {
	ch chan tea.Msg
}

type progressReader struct {
	reader     io.Reader
	total      int64
	read       int64
	onProgress func(float64)
}

func (pr *progressReader) Read(p []byte) (int, error) {
	n, err := pr.reader.Read(p)
	pr.read += int64(n)
	if pr.total > 0 {
		pr.onProgress(float64(pr.read) / float64(pr.total))
	}
	return n, err
}

func downloadWithProgressCmd(tag string) tea.Cmd {
	return func() tea.Msg {
		ch := make(chan tea.Msg, 100)
		go runDownload(tag, ch)
		return downloadStartedMsg{ch: ch}
	}
}

func runDownload(tag string, ch chan tea.Msg) {
	defer close(ch)

	url := buildDownloadURL(tag)
	tmpDir := os.TempDir()
	zipPath := filepath.Join(tmpDir, fmt.Sprintf("FluxDB-%s.zip", tag))

	client := &http.Client{Timeout: 10 * time.Minute}
	req, err := http.NewRequest("GET", url, nil)
	if err != nil {
		ch <- errMsg{err: fmt.Errorf("download-request erstellen fehlgeschlagen: %w", err)}
		return
	}
	req.Header.Set("User-Agent", "FluxDB-Installer")

	resp, err := client.Do(req)
	if err != nil {
		ch <- errMsg{err: fmt.Errorf("download fehlgeschlagen: %w", err)}
		return
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		ch <- errMsg{err: fmt.Errorf("download fehlgeschlagen: HTTP %s", resp.Status)}
		return
	}

	out, err := os.Create(zipPath)
	if err != nil {
		ch <- errMsg{err: fmt.Errorf("temporäre datei erstellen fehlgeschlagen: %w", err)}
		return
	}
	defer out.Close()

	pr := &progressReader{
		reader: resp.Body,
		total:  resp.ContentLength,
		onProgress: func(pct float64) {
			select {
			case ch <- downloadProgressMsg{percent: pct}:
			default:
			}
		},
	}

	if _, err := io.Copy(out, pr); err != nil {
		os.Remove(zipPath)
		ch <- errMsg{err: fmt.Errorf("download schreiben fehlgeschlagen: %w", err)}
		return
	}

	ch <- downloadCompleteMsg{path: zipPath}
}