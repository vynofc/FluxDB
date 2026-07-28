package main

import (
	"fmt"
	"time"

	"github.com/charmbracelet/bubbles/progress"
	"github.com/charmbracelet/bubbles/spinner"
	tea "github.com/charmbracelet/bubbletea"
	"github.com/charmbracelet/huh"
)

func (m model) Update(msg tea.Msg) (tea.Model, tea.Cmd) {
	switch msg := msg.(type) {
	case tea.WindowSizeMsg:
		m.width = msg.Width
		m.height = msg.Height
		m.progressBar.Width = msg.Width - 8
		if m.progressBar.Width > 60 {
			m.progressBar.Width = 60
		}
		m.viewport.Width = msg.Width - 8
		m.viewport.Height = m.height/3 - 2
		if m.viewport.Height < 4 {
			m.viewport.Height = 4
		}
		if m.viewport.Height > 20 {
			m.viewport.Height = 20
		}
		m.updateViewport()
		return m, nil

	case tea.KeyMsg:
		return m.handleKeyMsg(msg)

	case spinner.TickMsg:
		if m.state == stateLoading || m.state == stateExtracting || m.state == stateCreatingShortcut {
			var cmd tea.Cmd
			m.spinner, cmd = m.spinner.Update(msg)
			return m, cmd
		}
		return m, nil

	case progress.FrameMsg:
		if m.state == stateDownloading {
			progressModel, cmd := m.progressBar.Update(msg)
			m.progressBar = progressModel.(progress.Model)
			return m, cmd
		}
		return m, nil

	case logMsg:
		m.addLog(msg.line)
		return m, nil

	case releasesFetchedMsg:
		m.releases = msg.releases
		m.addLog(fmt.Sprintf("✅ %d Releases gefunden", len(msg.releases)))
		m.state = stateSelectVersion
		m.versionForm = buildVersionForm(msg.releases)
		return m, m.versionForm.Init()

	case tagFetchedMsg:
		m.tag = msg.tag
		m.addLog(fmt.Sprintf("📌 Version: %s", msg.tag))
		m.stepIndex = 1
		if m.customTag != "" {
			m.stepIndex = 1
		} else {
			m.stepIndex = 2
		}
		m.state = stateDownloading
		m.addLog(fmt.Sprintf("📥 Starte Download: %s", msg.tag))
		m.addLog("⚡ Verbindung wird aufgebaut...")
		m.progressCh = make(chan float64, 100)
		return m, tea.Batch(
			startDownloadCmd(msg.tag, m.progressCh),
			listenProgressCmd(m.progressCh),
			tickCmd(),
		)

	case downloadProgressMsg:
		progress := float64(msg)
		m.progress = progress
		cmd := m.progressBar.SetPercent(progress)
		return m, cmd

	case downloadCompleteMsg:
		m.zipPath = msg.path
		m.state = stateExtracting
		m.addLog("✅ Download abgeschlossen")
		m.addLog(fmt.Sprintf("📦 Gespeichert in: %s", msg.path))
		m.addLog("📂 Entpacke Dateien...")
		return m, tea.Batch(
			extractCmd(m.zipPath, m.customPath),
			m.spinner.Tick,
		)

	case extractCompleteMsg:
		m.installDir = msg.installDir
		m.addLog("✅ Entpacken abgeschlossen")
		m.addLog(fmt.Sprintf("📁 Installationspfad: %s", msg.installDir))
		m.stepIndex = m.totalSteps - 1
		m.state = stateAskShortcut
		m.shortcutForm = buildShortcutForm()
		return m, m.shortcutForm.Init()

	case shortcutCreatedMsg:
		m.addLog("🔗 Desktop-Verknuepfung erstellt")
		m.state = stateDone
		m.addLog("🎉 Installation abgeschlossen!")
		return m, nil

	case errMsg:
		m.state = stateError
		m.err = msg.err
		m.addLog(fmt.Sprintf("❌ Fehler: %s", msg.err.Error()))
		return m, nil
	}

	if m.state == stateSelectVersion && m.versionForm != nil {
		form, cmd := m.versionForm.Update(msg)
		if f, ok := form.(*huh.Form); ok {
			m.versionForm = f
		}
		if m.versionForm.State == huh.StateCompleted {
			m.selectedVersion = m.versionForm.GetString("")
			if m.selectedVersion == "" {
				var v string
				for _, r := range m.releases {
					v = r
					break
				}
				m.selectedVersion = v
			}
			m.tag = m.selectedVersion
			m.addLog(fmt.Sprintf("📌 Gewaehlte Version: %s", m.selectedVersion))
			m.addLog("📥 Bereite Download vor...")
			return m, func() tea.Msg {
				return tagFetchedMsg{tag: m.selectedVersion}
			}
		}
		return m, cmd
	}

	if m.state == stateAskShortcut && m.shortcutForm != nil {
		form, cmd := m.shortcutForm.Update(msg)
		if f, ok := form.(*huh.Form); ok {
			m.shortcutForm = f
		}
		if m.shortcutForm.State == huh.StateCompleted {
			m.createShortcut = m.shortcutForm.GetBool("")
			if m.createShortcut {
				m.state = stateCreatingShortcut
				m.addLog("🔗 Erstelle Desktop-Verknuepfung...")
				return m, tea.Batch(
					createShortcutCmd(m.installDir),
					m.spinner.Tick,
				)
			}
			m.addLog("ℹ️ Keine Verknuepfung erstellt")
			m.state = stateDone
			m.addLog("🎉 Installation abgeschlossen!")
			return m, nil
		}
		return m, cmd
	}

	return m, nil
}

func (m model) handleKeyMsg(msg tea.KeyMsg) (tea.Model, tea.Cmd) {
	key := msg.String()

	if key == "ctrl+c" {
		return m, tea.Quit
	}

	if m.state == stateDone || m.state == stateError {
		if key == "enter" || key == "q" {
			return m, tea.Quit
		}
		return m, nil
	}

	if m.state == stateSelectVersion && m.versionForm != nil {
		return m, nil
	}

	if m.state == stateAskShortcut && m.shortcutForm != nil {
		return m, nil
	}

	return m, nil
}

func listenProgressCmd(ch chan float64) tea.Cmd {
	return func() tea.Msg {
		for p := range ch {
			return downloadProgressMsg(p)
		}
		return nil
	}
}

func tickCmd() tea.Cmd {
	return tea.Every(time.Second/60, func(t time.Time) tea.Msg {
		return progress.FrameMsg{}
	})
}