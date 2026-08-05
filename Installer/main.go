package main

import (
	"flag"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"

	tea "github.com/charmbracelet/bubbletea"
	"github.com/charmbracelet/log"
)

func main() {
	customTag := flag.String("tag", "", "Bestimmte Version installieren (z.B. v1.0.0)")
	customPath := flag.String("path", "", "Alternatives Installationsverzeichnis")
	silent := flag.Bool("silent", false, "Keine TUI, nur Text-Output")
	silentStart := flag.Bool("silent-start", false, "Wie --silent, startet FluxDB nach der Installation automatisch")
	detail := flag.Bool("detail", false, "Detailmodus: Versionsauswahl + ausfuehrliches Log")
	flag.Parse()

	logger := log.New(os.Stderr)
	logger.SetLevel(log.DebugLevel)

	if *silent || *silentStart {
		runSilent(*customTag, *customPath, *silentStart, logger)
		return
	}

	m := initialModel(*customTag, *customPath, *detail)
	p := tea.NewProgram(&m, tea.WithAltScreen())

	if _, err := p.Run(); err != nil {
		fmt.Fprintf(os.Stderr, "Fehler: %v\n", err)
		os.Exit(1)
	}
}

func launchFluxDB(installDir string, logger *log.Logger) {
	exe := filepath.Join(installDir, "FluxDB.exe")
	cmd := exec.Command(exe)
	cmd.Dir = installDir
	if err := cmd.Start(); err != nil {
		logger.Warn("FluxDB konnte nicht gestartet werden", "error", err)
	}
}

func runSilent(customTag, customPath string, startAfter bool, logger *log.Logger) {
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
		extractResult := extractCmd(m.path, customPath, tag)()
		switch em := extractResult.(type) {
		case extractCompleteMsg:
			logger.Info(fmt.Sprintf("FluxDB %s erfolgreich installiert!", tag))
			logger.Info(fmt.Sprintf("Installationspfad: %s", em.installDir))
			if startAfter {
				launchFluxDB(em.installDir, logger)
			}
		case errMsg:
			logger.Error("Entpacken fehlgeschlagen", "error", em.err)
			os.Exit(1)
		}
	case errMsg:
		logger.Error("Download fehlgeschlagen", "error", m.err)
		os.Exit(1)
	}
}
