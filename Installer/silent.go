package main

import (
	"os"
	"path/filepath"
)

func init() {
	if os.Getenv("LOCALAPPDATA") == "" {
		home, err := os.UserHomeDir()
		if err != nil {
			home = os.Getenv("USERPROFILE")
		}
		if home == "" {
			return
		}
		os.Setenv("LOCALAPPDATA", filepath.Join(home, "AppData", "Local"))
	}
}