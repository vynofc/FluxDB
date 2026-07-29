package main

import (
	"os"
	"path/filepath"
)

func init() {
	if os.Getenv("LOCALAPPDATA") == "" {
		home, _ := os.UserHomeDir()
		os.Setenv("LOCALAPPDATA", filepath.Join(home, "AppData", "Local"))
	}
}