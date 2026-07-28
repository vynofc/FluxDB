package main

import (
	"encoding/json"
	"fmt"
	"net/http"
	"time"

	tea "github.com/charmbracelet/bubbletea"
)

const (
	githubAPIURL = "https://api.github.com/repos/vynofc/FluxDB/releases/latest"
	downloadURL  = "https://github.com/vynofc/FluxDB/releases/download/%s/FluxDB.zip"
)

type release struct {
	TagName string `json:"tag_name"`
}

func fetchTagCmd(customTag string) tea.Cmd {
	return func() tea.Msg {
		if customTag != "" {
			return tagFetchedMsg{tag: customTag}
		}

		client := &http.Client{Timeout: 15 * time.Second}
		req, err := http.NewRequest("GET", githubAPIURL, nil)
		if err != nil {
			return errMsg{err: fmt.Errorf("request erstellen fehlgeschlagen: %w", err)}
		}
		req.Header.Set("User-Agent", "FluxDB-Installer")
		req.Header.Set("Accept", "application/vnd.github+json")

		resp, err := client.Do(req)
		if err != nil {
			return errMsg{err: fmt.Errorf("GitHub API nicht erreichbar: %w", err)}
		}
		defer resp.Body.Close()

		if resp.StatusCode == http.StatusForbidden || resp.StatusCode == http.StatusTooManyRequests {
			return errMsg{err: fmt.Errorf("GitHub API-Limit erreicht — bitte nutze --tag <version> um eine bestimmte Version zu installieren")}
		}

		if resp.StatusCode != http.StatusOK {
			return errMsg{err: fmt.Errorf("GitHub API Fehler: %s", resp.Status)}
		}

		var rel release
		if err := json.NewDecoder(resp.Body).Decode(&rel); err != nil {
			return errMsg{err: fmt.Errorf("JSON-Dekodierung fehlgeschlagen: %w", err)}
		}

		if rel.TagName == "" {
			return errMsg{err: fmt.Errorf("kein Release-Tag gefunden")}
		}

		return tagFetchedMsg{tag: rel.TagName}
	}
}

func buildDownloadURL(tag string) string {
	return fmt.Sprintf(downloadURL, tag)
}