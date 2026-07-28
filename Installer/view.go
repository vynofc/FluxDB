package main

import (
	"fmt"
	"strings"
)

func (m model) View() string {
	var s strings.Builder

	s.WriteString(titleStyle.Render("FluxDB Installer"))
	s.WriteString("\n")

	s.WriteString(m.renderSteps())
	s.WriteString("\n")

	s.WriteString(m.renderContent())
	s.WriteString("\n\n")

	s.WriteString(m.viewport.View())
	s.WriteString("\n")

	s.WriteString(m.renderFooter())

	return appStyle.Render(s.String())
}

func (m model) renderSteps() string {
	var parts []string
	steps := m.buildStepList()

	for i, step := range steps {
		stepNum := i + 1
		done := stepNum < m.stepIndex || (stepNum == m.stepIndex && m.state == stateDone)
		current := stepNum == m.stepIndex && m.state != stateDone && m.state != stateError
		parts = append(parts, formatStepHeader(stepNum, len(steps), step, done, current))
	}

	return strings.Join(parts, "\n") + "\n" + dividerStyle(strings.Repeat("─", 60))
}

func (m model) buildStepList() []string {
	steps := []string{"Releases abrufen"}
	if m.customTag == "" {
		steps = append(steps, "Version waehlen")
	}
	steps = append(steps, "Download")
	steps = append(steps, "Entpacken")
	steps = append(steps, "Abschluss")
	return steps
}

func (m model) renderContent() string {
	var s strings.Builder

	switch m.state {
	case stateLoading:
		s.WriteString(m.spinner.View())
		s.WriteString(" ")
		s.WriteString(statusStyle.Render("Ermittle verfuegbare Versionen..."))

	case stateSelectVersion:
		if m.versionForm != nil {
			s.WriteString(statusStyle.Render("Waehle eine Version aus:"))
			s.WriteString("\n\n")
			s.WriteString(m.versionForm.View())
		}

	case stateDownloading:
		s.WriteString(statusStyle.Render(fmt.Sprintf("Lade FluxDB %s herunter...", m.tag)))
		s.WriteString("\n\n")
		s.WriteString(m.progressBar.View())
		s.WriteString("\n")
		pct := fmt.Sprintf("%.0f%%", m.progress*100)
		s.WriteString(dimStyle.Render(pct))
		s.WriteString("  ")
		s.WriteString(dimStyle.Render(buildDownloadURL(m.tag)))

	case stateExtracting:
		s.WriteString(m.spinner.View())
		s.WriteString(" ")
		s.WriteString(statusStyle.Render(fmt.Sprintf("Entpacke FluxDB %s...", m.tag)))

	case stateAskShortcut:
		if m.shortcutForm != nil {
			s.WriteString(m.shortcutForm.View())
		}

	case stateCreatingShortcut:
		s.WriteString(m.spinner.View())
		s.WriteString(" ")
		s.WriteString(statusStyle.Render("Erstelle Desktop-Verknuepfung..."))

	case stateDone:
		installDir := m.installDir
		if installDir == "" {
			installDir = m.customPath
		}
		if installDir == "" {
			installDir = "%LOCALAPPDATA%\\FluxDB"
		}
		s.WriteString(successStyle.Render(fmt.Sprintf("✓ FluxDB %s erfolgreich installiert!", m.tag)))
		s.WriteString("\n\n")
		s.WriteString(dimStyle.Render(fmt.Sprintf("Installationspfad: %s", installDir)))
		s.WriteString("\n")
		if m.createShortcut {
			s.WriteString(successStyle.Render("Desktop-Verknuepfung wurde erstellt"))
		}
		s.WriteString("\n")
		s.WriteString(helpStyle.Render("Druecke Enter oder Q zum Beenden"))

	case stateError:
		s.WriteString(errorStyle.Render("✗ Installation fehlgeschlagen"))
		s.WriteString("\n\n")
		s.WriteString(errorStyle.Render(m.err.Error()))
		s.WriteString("\n")
		s.WriteString(helpStyle.Render("Druecke Enter oder Q zum Beenden"))
	}

	return s.String()
}

func (m model) renderFooter() string {
	if m.state == stateDone || m.state == stateError {
		return helpStyle.Render("Enter/Q = Beenden")
	}
	if m.state == stateSelectVersion || m.state == stateAskShortcut {
		return helpStyle.Render("↑↓ = Navigieren  |  Enter = Auswaehlen  |  Ctrl+C = Abbrechen")
	}
	return helpStyle.Render("Ctrl+C = Abbrechen")
}