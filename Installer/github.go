package main

import (
	"encoding/json"
	"fmt"
	"net/http"
	"time"

	tea "github.com/charmbracelet/bubbletea"
)

const (
	githubAPIURL     = "https://api.github.com/repos/vynofc/FluxDB/releases"
	downloadBaseURL  = "https://github.com/vynofc/FluxDB/releases/download/%s/FluxDB.zip"
)

type githubRelease struct {
	TagName    string `json:"tag_name"`
	Prerelease bool   `json:"prerelease"`
}

type releaseInfo struct {
	tag        string
	prerelease bool
}

func fetchReleasesCmd() tea.Cmd {
	return func() tea.Msg {
		client := &http.Client{Timeout: 15 * time.Second}
		req, err := http.NewRequest("GET", githubAPIURL+"?per_page=20", nil)
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
			return errMsg{err: fmt.Errorf("GitHub API-Limit erreicht")}
		}

		if resp.StatusCode != http.StatusOK {
			return errMsg{err: fmt.Errorf("GitHub API Fehler: %s", resp.Status)}
		}

		var releases []githubRelease
		if err := json.NewDecoder(resp.Body).Decode(&releases); err != nil {
			return errMsg{err: fmt.Errorf("JSON-Dekodierung fehlgeschlagen: %w", err)}
		}

		if len(releases) == 0 {
			return errMsg{err: fmt.Errorf("keine Releases gefunden")}
		}

		var infos []releaseInfo
		for _, r := range releases {
			infos = append(infos, releaseInfo{tag: r.TagName, prerelease: r.Prerelease})
		}

		return releasesFetchedMsg{releases: infos}
	}
}

func fetchLatestTagCmd(includeBeta bool) tea.Cmd {
	return func() tea.Msg {
		client := &http.Client{Timeout: 15 * time.Second}
		req, err := http.NewRequest("GET", githubAPIURL+"?per_page=20", nil)
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
			return errMsg{err: fmt.Errorf("GitHub API-Limit erreicht")}
		}

		if resp.StatusCode != http.StatusOK {
			return errMsg{err: fmt.Errorf("GitHub API Fehler: %s", resp.Status)}
		}

		var allReleases []githubRelease
		if err := json.NewDecoder(resp.Body).Decode(&allReleases); err != nil {
			return errMsg{err: fmt.Errorf("JSON-Dekodierung fehlgeschlagen: %w", err)}
		}

		if len(allReleases) == 0 {
			return errMsg{err: fmt.Errorf("keine Releases gefunden")}
		}

		if includeBeta {
			return tagFetchedMsg{tag: allReleases[0].TagName}
		}

		for _, r := range allReleases {
			if !r.Prerelease {
				return tagFetchedMsg{tag: r.TagName}
			}
		}

		return errMsg{err: fmt.Errorf("keine Stable-Releases gefunden")}
	}
}

func buildDownloadURL(tag string) string {
	return fmt.Sprintf(downloadBaseURL, tag)
}