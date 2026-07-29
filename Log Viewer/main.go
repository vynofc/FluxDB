package main

import (
	"flag"
	"fmt"
	"os"

	tea "github.com/charmbracelet/bubbletea"
)

func main() {
	logPath := flag.String("log", "", "Pfad zur Logdatei (erforderlich)")
	flag.Parse()

	if *logPath == "" {
		fmt.Fprintf(os.Stderr, "Fehler: --log <pfad> ist erforderlich\n")
		os.Exit(1)
	}

	m := initialModel(*logPath)
	p := tea.NewProgram(&m, tea.WithAltScreen())

	if _, err := p.Run(); err != nil {
		fmt.Fprintf(os.Stderr, "Fehler: %v\n", err)
		os.Exit(1)
	}
}