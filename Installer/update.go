package main

import (
	"fmt"
	"fmt"
	"time"

	"github.com/charmbracelet/bubbles/progress"
	"github.com/charmbracelet/bubbles/spinner"
	tea "github.com/charmbracelet/bubbletea"
	"github.com/charmbracelet/huh"
)

func (m *model) Update(msg tea.Msg) (tea.Model, tea.Cmd) {
	if keyMsg, ok := msg.(tea.KeyMsg); ok {
		if keyMsg.String() == "ctrl+c" {
			return m, tea.Quit
		}
	}

	if m.state == stateSelectVersion && m.versionForm != nil {
		return m.handleVersionForm(msg)
	}

	if m.state == stateAskShortcut && m.shortcutForm != nil {
		return m.handleShortcutForm(msg)
	}

	if keyMsg, ok := msg.(tea.KeyMsg); ok {
		if m.state == stateDone || m.state == stateError {
			if keyMsg.String() == "enter" || keyMsg.String() == "q" {
				return m, tea.Quit
			}
		}
		return m, nil
	}

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
		if m.detail {
			m.addLog(msg.line)
		}
		return m, nil

	case releasesFetchedMsg:
		m.releases = msg.releases
		if m.detail {
			m.addLog(fmt.Sprintf("✅ %d Releases gefunden", len(msg.releases)))
		}
		m.nextStep()
		m.state = stateSelectVersion
		m.versionForm = buildVersionForm(msg.releases, &m.selectedVersion)
		return m, m.versionForm.Init()

	case tagFetchedMsg:
		m.tag = msg.tag
		if m.detail {
			m.addLog(fmt.Sprintf("📌 Version: %s", msg.tag))
		}
		m.nextStep()
		m.state = stateDownloading
		if m.detail {
			m.addLog(fmt.Sprintf("📥 Starte Download: %s", msg.tag))
			m.addLog("⚡ Verbindung wird aufgebaut...")
		}
		m.progressCh = make(chan float64, 100)
		return m, tea.Batch(
			startDownloadCmd(msg.tag, m.progressCh),
			listenProgress(m.progressCh),
			tickEvery(),
		)

	case downloadStartedMsg:
		return m, waitForDownloadCmd(msg.ch)

	case downloadProgressMsg:
		progress := float64(msg)
		m.progress = progress
		cmd := m.progressBar.SetPercent(progress)
		return m, tea.Batch(cmd, listenProgress(m.progressCh))

	case downloadCompleteMsg:
		m.zipPath = msg.path
		m.state = stateExtracting
		m.nextStep()
		if m.detail {
			m.addLog("✅ Download abgeschlossen")
			m.addLog(fmt.Sprintf("📦 Gespeichert in: %s", msg.path))
			m.addLog("📂 Entpacke Dateien...")
		}
		return m, tea.Batch(
			extractCmd(m.zipPath, m.customPath),
			m.spinner.Tick,
		)

	case extractCompleteMsg:
		m.installDir = msg.installDir
		m.nextStep()
		if m.detail {
			m.addLog("✅ Entpacken abgeschlossen")
			m.addLog(fmt.Sprintf("📁 Installationspfad: %s", msg.installDir))
		}
		m.state = stateAskShortcut
		m.shortcutForm = buildShortcutForm(&m.createShortcut)
		return m, m.shortcutForm.Init()

	case shortcutCreatedMsg:
		m.markAllStepsDone()
		if m.detail {
			m.addLog("🔗 Verknuepfungen erstellt")
		}
		m.state = stateDone
		if m.detail {
			m.addLog("🎉 Installation abgeschlossen!")
		}
		return m, nil

	case errMsg:
		m.state = stateError
		m.err = msg.err
		if m.detail {
			m.addLog(fmt.Sprintf("❌ Fehler: %s", msg.err.Error()))
		}
		return m, nil
	}

	return m, nil
}

func (m *model) handleVersionForm(msg tea.Msg) (tea.Model, tea.Cmd) {
	form, cmd := m.versionForm.Update(msg)
	if f, ok := form.(*huh.Form); ok {
		m.versionForm = f
	}
	if m.versionForm.State == huh.StateCompleted {
		if m.selectedVersion == "" && len(m.releases) > 0 {
			m.selectedVersion = m.releases[0]
		}
		m.tag = m.selectedVersion
		if m.detail {
			m.addLog(fmt.Sprintf("📌 Gewaehlte Version: %s", m.selectedVersion))
		}
		return m, func() tea.Msg { return tagFetchedMsg{tag: m.selectedVersion} }
	}
	return m, cmd
}

func (m *model) handleShortcutForm(msg tea.Msg) (tea.Model, tea.Cmd) {
	form, cmd := m.shortcutForm.Update(msg)
	if f, ok := form.(*huh.Form); ok {
		m.shortcutForm = f
	}
	if m.shortcutForm.State == huh.StateCompleted {
		if m.createShortcut {
			m.state = stateCreatingShortcut
			if m.detail {
				m.addLog("🔗 Erstelle Verknuepfungen...")
			}
			return m, tea.Batch(
				createShortcutsCmd(m.installDir),
				m.spinner.Tick,
			)
		}
		m.markAllStepsDone()
		if m.detail {
			m.addLog("ℹ️ Keine Verknuepfungen erstellt")
		}
		m.state = stateDone
		if m.detail {
			m.addLog("🎉 Installation abgeschlossen!")
		}
		return m, nil
	}
	return m, cmd
}

func listenProgress(ch chan float64) tea.Cmd {
	return func() tea.Msg {
		for p := range ch {
			return downloadProgressMsg(p)
		}
		return nil
	}
}

func tickEvery() tea.Cmd {
	return tea.Every(time.Second/60, func(t time.Time) tea.Msg {
		return progress.FrameMsg{}
	})
}