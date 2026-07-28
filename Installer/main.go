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

	m := initialModel(*customTag, *customPath)
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
		msg := fetchLatestTagCmd()()
		switch m := msg.(type) {
		case tagFetchedMsg:
			tag = m.tag
		case errMsg:
			logger.Error("Tag konnte nicht ermittelt werden", "error", m.err)
			os.Exit(1)
		default:
			logger.Error("unerwarteter Fehler")
			os.Exit(1)
		}
	}

	logger.Info("Version ermittelt", "tag", tag)
	logger.Info("Download startet...", "tag", tag)

	progressCh := make(chan float64, 100)
	dlDone := make(chan tea.Msg, 1)

	go func() {
		dlDone <- startDownloadCmd(tag, progressCh)()
	}()

	go func() {
		for p := range progressCh {
			logger.Info(fmt.Sprintf("Download: %.0f%%", p*100))
		}
	}()

	dlResult := <-dlDone
	switch m := dlResult.(type) {
	case downloadCompleteMsg:
		logger.Info("Download abgeschlossen", "path", m.path)
		logger.Info("Entpacke...")
		extractResult := extractCmd(m.path, customPath)()
		switch em := extractResult.(type) {
		case extractCompleteMsg:
			logger.Info(fmt.Sprintf("FluxDB %s erfolgreich installiert!", tag))
			logger.Info(fmt.Sprintf("Installationspfad: %s", em.installDir))
		case errMsg:
			logger.Error("Entpacken fehlgeschlagen", "error", em.err)
			os.Exit(1)
		}
	case errMsg:
		logger.Error("Download fehlgeschlagen", "error", m.err)
		os.Exit(1)
	}
}