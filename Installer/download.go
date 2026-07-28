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

func startDownloadCmd(tag string, progressCh chan float64) tea.Cmd {
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

		client := &http.Client{Timeout: 30 * time.Minute}
		req, err := http.NewRequest("GET", url, nil)
		if err != nil {
			return errMsg{err: fmt.Errorf("download-request erstellen fehlgeschlagen: %w", err)}
		}
		req.Header.Set("User-Agent", "FluxDB-Installer")

	resp, err := client.Do(req)
	if err != nil {
		ch <- errMsg{err: fmt.Errorf("download fehlgeschlagen: %w", err)}
		return
	}
	defer resp.Body.Close()

		if resp.StatusCode != http.StatusOK {
			if resp.StatusCode == http.StatusNotFound {
				return errMsg{err: fmt.Errorf("Release %s nicht gefunden", tag)}
			}
			return errMsg{err: fmt.Errorf("download fehlgeschlagen: HTTP %s", resp.Status)}
		}

		out, err := os.Create(zipPath)
		if err != nil {
			return errMsg{err: fmt.Errorf("temporaere datei erstellen fehlgeschlagen: %w", err)}
		}
		defer out.Close()

		totalSize := resp.ContentLength
		reader := &progressReader{
			reader:    resp.Body,
			total:     totalSize,
			progressCh: progressCh,
			onProgress: func(downloaded, total int64) {
				if total > 0 {
					select {
					case progressCh <- float64(downloaded) / float64(total):
					default:
					}
				}
			},
		}

		_, err = io.Copy(out, reader)
		close(progressCh)

		if err != nil {
			os.Remove(zipPath)
			return errMsg{err: fmt.Errorf("download schreiben fehlgeschlagen: %w", err)}
		}

		return downloadCompleteMsg{path: zipPath}
	}
}

type progressReader struct {
	reader     io.Reader
	total      int64
	downloaded int64
	progressCh chan float64
	onProgress func(int64, int64)
	lastLog    int64
}

func (pr *progressReader) Read(p []byte) (int, error) {
	n, err := pr.reader.Read(p)
	pr.downloaded += int64(n)

	if pr.onProgress != nil {
		pr.onProgress(pr.downloaded, pr.total)
	}

	if pr.downloaded-pr.lastLog > 5*1024*1024 {
		pr.lastLog = pr.downloaded
	}

	return n, err
}