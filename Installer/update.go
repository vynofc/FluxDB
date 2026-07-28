package main

import (
	"time"

	"github.com/charmbracelet/bubbles/progress"
	"github.com/charmbracelet/bubbles/spinner"
	tea "github.com/charmbracelet/bubbletea"
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
		return m, nil

	case tea.KeyMsg:
		if msg.String() == "q" || msg.String() == "ctrl+c" {
			return m, tea.Quit
		}
		if m.state == stateDone || m.state == stateError {
			if msg.String() == "enter" {
				return m, tea.Quit
			}
		}
		return m, nil

	case spinner.TickMsg:
		if m.state == stateFetchingTag || m.state == stateExtracting {
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

	case tagFetchedMsg:
		m.tag = msg.tag
		m.state = stateDownloading
		return m, tea.Batch(
			downloadWithProgressCmd(m.tag),
			tickCmd(),
		)

	case downloadProgressMsg:
		m.progress = msg.percent
		cmd := m.progressBar.SetPercent(msg.percent)
		return m, cmd

	case downloadCompleteMsg:
		m.zipPath = msg.path
		m.state = stateExtracting
		return m, tea.Batch(
			extractCmd(m.zipPath, m.customPath),
			m.spinner.Tick,
		)

	case extractCompleteMsg:
		m.state = stateDone
		return m, nil

	case errMsg:
		m.state = stateError
		m.err = msg.err
		return m, nil
	}

	return m, nil
}

func tickCmd() tea.Cmd {
	return tea.Every(time.Second/60, func(t time.Time) tea.Msg {
		return progress.FrameMsg{}
	})
}