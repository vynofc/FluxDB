package main

import (
	"fmt"
	"io"
	"net/http"
	"os"
	"time"

	tea "github.com/charmbracelet/bubbletea"
)

func startDownloadCmd(tag string, progressCh chan float64) tea.Cmd {
	return func() tea.Msg {
		url := buildDownloadURL(tag)
		tmpDir := os.TempDir()
		out, err := os.CreateTemp(tmpDir, fmt.Sprintf("FluxDB-%s-*.zip", tag))
		if err != nil {
			close(progressCh)
			return errMsg{err: fmt.Errorf("temporaere datei erstellen fehlgeschlagen: %w", err)}
		}
		zipPath := out.Name()

		client := &http.Client{Timeout: 30 * time.Minute}
		req, err := http.NewRequest("GET", url, nil)
		if err != nil {
			os.Remove(zipPath)
			out.Close()
			close(progressCh)
			return errMsg{err: fmt.Errorf("download-request erstellen fehlgeschlagen: %w", err)}
		}
		req.Header.Set("User-Agent", "FluxDB-Installer")

		resp, err := client.Do(req)
		if err != nil {
			os.Remove(zipPath)
			out.Close()
			close(progressCh)
			return errMsg{err: fmt.Errorf("download fehlgeschlagen: %w", err)}
		}
		defer resp.Body.Close()

		if resp.StatusCode != http.StatusOK {
			os.Remove(zipPath)
			out.Close()
			close(progressCh)
			if resp.StatusCode == http.StatusNotFound {
				return errMsg{err: fmt.Errorf("Release %s nicht gefunden", tag)}
			}
			return errMsg{err: fmt.Errorf("download fehlgeschlagen: HTTP %s", resp.Status)}
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