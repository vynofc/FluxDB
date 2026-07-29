package main

import (
	"bufio"
	"os"
	"strings"

	tea "github.com/charmbracelet/bubbletea"
)

func (m *model) Update(msg tea.Msg) (tea.Model, tea.Cmd) {
	switch msg := msg.(type) {
	case tea.WindowSizeMsg:
		m.width = msg.Width
		m.height = msg.Height
		m.viewport.Width = msg.Width - 8
		m.viewport.Height = m.height - 8
		if m.viewport.Width < 40 {
			m.viewport.Width = 40
		}
		if m.viewport.Height < 8 {
			m.viewport.Height = 8
		}
		if m.ready {
			m.updateViewportContent()
		}
		return m, nil

	case tea.KeyMsg:
		return m.handleKeyMsg(msg)

	case logLinesMsg:
		m.logLines = msg
		m.lastSize = -1
		info, err := os.Stat(m.logPath)
		if err == nil {
			m.lastSize = info.Size()
		}
		m.viewport.SetContent(m.renderLogLines())
		m.viewport.GotoBottom()
		m.ready = true
		if m.searchTerm != "" {
			m.findMatches()
		}
		return m, nil

	case tailTickMsg:
		m.tail()
		return m, tailTickCmd()

	case errMsg:
		return m, nil
	}

	return m, nil
}

func (m *model) handleKeyMsg(msg tea.KeyMsg) (tea.Model, tea.Cmd) {
	switch msg.String() {
	case "ctrl+c", "q":
		if m.state == stateSearching {
			m.state = stateViewing
			m.searchInput = ""
			m.searchTerm = ""
			m.matchedLines = nil
			m.searchIdx = 0
			m.updateViewportContent()
			return m, nil
		}
		return m, tea.Quit

	case "esc":
		if m.state == stateSearching {
			m.state = stateViewing
			m.searchInput = ""
			m.searchTerm = ""
			m.matchedLines = nil
			m.searchIdx = 0
			m.updateViewportContent()
			return m, nil
		}
		return m, tea.Quit

	case "enter":
		if m.state == stateSearching {
			if m.searchInput != "" {
				m.searchTerm = m.searchInput
				m.findMatches()
			}
			m.state = stateViewing
			return m, nil
		}
		return m, nil

	case "/":
		if m.state == stateViewing {
			m.state = stateSearching
			m.searchInput = ""
			return m, nil
		}
		return m, nil

	case "r":
		if m.state == stateViewing {
			return m, loadLogFileCmd(m.logPath)
		}
		return m, nil

	case "c":
		if m.state == stateViewing {
			return m, clearLogFileCmd(m.logPath)
		}
		return m, nil

	case "pgup", "u":
		if m.state == stateViewing {
			m.viewport.HalfViewUp()
		}
		return m, nil

	case "pgdown", "d":
		if m.state == stateViewing {
			m.viewport.HalfViewDown()
		}
		return m, nil

	case "home":
		if m.state == stateViewing {
			m.viewport.GotoTop()
		}
		return m, nil

	case "end":
		if m.state == stateViewing {
			m.viewport.GotoBottom()
		}
		return m, nil

	case "n":
		if m.state == stateViewing && m.searchTerm != "" {
			m.nextMatch()
		}
		return m, nil

	case "backspace":
		if m.state == stateSearching && len(m.searchInput) > 0 {
			m.searchInput = m.searchInput[:len(m.searchInput)-1]
		}
		return m, nil
	}

	if m.state == stateSearching {
		if len(msg.String()) == 1 {
			m.searchInput += msg.String()
		}
		return m, nil
	}

	return m, nil
}

func (m *model) findMatches() {
	m.matchedLines = nil
	lowerTerm := strings.ToLower(m.searchTerm)
	for i, line := range m.logLines {
		if strings.Contains(strings.ToLower(line), lowerTerm) {
			m.matchedLines = append(m.matchedLines, i)
		}
	}
	m.searchIdx = 0
	if len(m.matchedLines) > 0 {
		m.viewport.SetYOffset(m.matchedLines[0])
	}
	m.updateViewportContent()
}

func (m *model) nextMatch() {
	if len(m.matchedLines) == 0 {
		return
	}
	m.searchIdx++
	if m.searchIdx >= len(m.matchedLines) {
		m.searchIdx = 0
	}
	m.viewport.SetYOffset(m.matchedLines[m.searchIdx])
	m.updateViewportContent()
}

func (m *model) tail() {
	info, err := os.Stat(m.logPath)
	if err != nil {
		return
	}

	if m.lastSize < 0 || info.Size() < m.lastSize {
		m.lastSize = info.Size()
		return
	}

	if info.Size() > m.lastSize {
		f, err := os.Open(m.logPath)
		if err != nil {
			return
		}
		defer f.Close()

		f.Seek(m.lastSize, 0)
		scanner := bufio.NewScanner(f)
		for scanner.Scan() {
			m.logLines = append(m.logLines, scanner.Text())
		}
		m.lastSize = info.Size()

		if m.searchTerm != "" {
			m.findMatches()
		}
		m.updateViewportContent()
	}
}