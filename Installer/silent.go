package main

import (
	"fmt"
	"os"
	"path/filepath"
)

func fetchTag(customTag string) (string, error) {
	msg := fetchTagCmd(customTag)()
	switch m := msg.(type) {
	case tagFetchedMsg:
		return m.tag, nil
	case errMsg:
		return "", m.err
	}
	return "", fmt.Errorf("unerwarteter fehler beim tag-fetch")
}

func downloadSilent(tag string) (string, error) {
	msg := downloadWithProgressCmd(tag)()
	switch m := msg.(type) {
	case downloadCompleteMsg:
		return m.path, nil
	case errMsg:
		return "", m.err
	}
	return "", fmt.Errorf("unerwarteter fehler beim download")
}

func extractSilent(zipPath, customPath string) error {
	msg := extractCmd(zipPath, customPath)()
	switch m := msg.(type) {
	case extractCompleteMsg:
		return nil
	case errMsg:
		return m.err
	}
	return fmt.Errorf("unerwarteter fehler beim entpacken")
}

func init() {
	if os.Getenv("LOCALAPPDATA") == "" {
		home, _ := os.UserHomeDir()
		os.Setenv("LOCALAPPDATA", filepath.Join(home, "AppData", "Local"))
	}
}