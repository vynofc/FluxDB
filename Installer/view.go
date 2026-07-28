package main

import (
	"fmt"
	"strings"
)

func (m model) View() string {
	if m.silent {
		return m.silentView()
	}

	var s strings.Builder

	s.WriteString(titleStyle.Render("FluxDB Installer"))
	s.WriteString("\n\n")

	switch m.state {
	case stateFetchingTag:
		s.WriteString(m.spinner.View())
		s.WriteString(" ")
		s.WriteString(statusStyle.Render("Ermittle neueste Version..."))

	case stateDownloading:
		s.WriteString(statusStyle.Render(fmt.Sprintf("Lade FluxDB %s herunter...", m.tag)))
		s.WriteString("\n\n")
		s.WriteString(m.progressBar.View())
		s.WriteString("\n")
		s.WriteString(dimStyle.Render(fmt.Sprintf("https://github.com/vynofc/FluxDB/releases/download/%s/FluxDB.zip", m.tag)))

	case stateExtracting:
		s.WriteString(m.spinner.View())
		s.WriteString(" ")
		s.WriteString(statusStyle.Render(fmt.Sprintf("Entpacke FluxDB %s...", m.tag)))

	case stateDone:
		installDir := m.customPath
		if installDir == "" {
			installDir = "%LOCALAPPDATA%\\FluxDB"
		}
		s.WriteString(successStyle.Render(fmt.Sprintf("✓ FluxDB %s erfolgreich installiert!", m.tag)))
		s.WriteString("\n\n")
		s.WriteString(dimStyle.Render(fmt.Sprintf("Installationspfad: %s", installDir)))
		s.WriteString("\n")
		s.WriteString(helpStyle.Render("Drücke Enter zum Beenden"))

	case stateError:
		s.WriteString(errorStyle.Render("✗ Installation fehlgeschlagen"))
		s.WriteString("\n\n")
		s.WriteString(errorStyle.Render(m.err.Error()))
		s.WriteString("\n")
		s.WriteString(helpStyle.Render("Drücke Enter zum Beenden"))
	}

	return appStyle.Render(s.String())
}

func (m model) silentView() string {
	switch m.state {
	case stateFetchingTag:
		return "Ermittle neueste Version..."
	case stateDownloading:
		return fmt.Sprintf("Lade FluxDB %s herunter...", m.tag)
	case stateExtracting:
		return fmt.Sprintf("Entpacke FluxDB %s...", m.tag)
	case stateDone:
		return fmt.Sprintf("FluxDB %s erfolgreich installiert!", m.tag)
	case stateError:
		return fmt.Sprintf("Fehler: %s", m.err.Error())
	}
	return ""
}