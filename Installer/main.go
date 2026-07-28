package main

import (
	"flag"
	"fmt"
	"os"

	tea "github.com/charmbracelet/bubbletea"
	"github.com/charmbracelet/log"
)

func main() {
	customTag := flag.String("tag", "", "Bestimmte Version installieren (z.B. v1.0.0)")
	customPath := flag.String("path", "", "Alternatives Installationsverzeichnis")
	silent := flag.Bool("silent", false, "Keine TUI, nur Text-Output")
	flag.Parse()

	logger := log.New(os.Stderr)
	logger.SetLevel(log.DebugLevel)

	if *silent {
		runSilent(*customTag, *customPath, logger)
		return
	}

	m := initialModel(*customTag, *customPath, false)
	p := tea.NewProgram(m, tea.WithAltScreen())

	if _, err := p.Run(); err != nil {
		fmt.Fprintf(os.Stderr, "Fehler: %v\n", err)
		os.Exit(1)
	}
}

func runSilent(customTag, customPath string, logger *log.Logger) {
	var tag string
	if customTag != "" {
		tag = customTag
	} else {
		var err error
		tag, err = fetchTag(customTag)
		if err != nil {
			logger.Error("Tag konnte nicht ermittelt werden", "error", err)
			os.Exit(1)
		}
	}

	logger.Info("Version ermittelt", "tag", tag)
	logger.Info("Download startet...", "tag", tag)

	zipPath, err := downloadSilent(tag)
	if err != nil {
		logger.Error("Download fehlgeschlagen", "error", err)
		os.Exit(1)
	}

	logger.Info("Download abgeschlossen", "path", zipPath)
	logger.Info("Entpacke...")

	if err := extractSilent(zipPath, customPath); err != nil {
		logger.Error("Entpacken fehlgeschlagen", "error", err)
		os.Exit(1)
	}

	logger.Info(fmt.Sprintf("FluxDB %s erfolgreich installiert!", tag))
}