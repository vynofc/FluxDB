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
		m.viewport.Width = msg.Width
		m.viewport.Height = msg.Height - 2
		if m.ready {
			m.viewport.SetContent(strings.Join(m.styledLines, "\n"))
		}
		return m, nil

	case tea.KeyMsg:
		return m.handleKey(msg)

	case logLinesMsg:
		m.setLines(msg)
		return m, nil

	case tailTickMsg:
		m.tail()
		return m, tailTick()

	case errMsg:
		return m, nil
	}

	return m, nil
}

func (m *model) handleKey(msg tea.KeyMsg) (tea.Model, tea.Cmd) {
	switch msg.String() {
	case "ctrl+c", "esc", "q":
		return m, tea.Quit
	case "r":
		return m, loadLogCmd(m.logPath)
	case "up", "k":
		m.viewport.LineUp(1)
		return m, nil
	case "down", "j":
		m.viewport.LineDown(1)
		return m, nil
	case "h":
		m.viewport.GotoTop()
		return m, nil
	case "b":
		m.viewport.GotoBottom()
		return m, nil
	}
	return m, nil
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
	if info.Size() == m.lastSize {
		return
	}

	f, err := os.Open(m.logPath)
	if err != nil {
		return
	}
	defer f.Close()

	f.Seek(m.lastSize, 0)
	scanner := bufio.NewScanner(f)
	scanner.Buffer(make([]byte, 1024*1024), 1024*1024)
	for scanner.Scan() {
		raw := scanner.Text()
		m.lines = append(m.lines, raw)
		m.styledLines = append(m.styledLines, styleLogLine(raw))
	}
	m.lastSize = info.Size()
	m.viewport.SetContent(strings.Join(m.styledLines, "\n"))
	m.viewport.GotoBottom()
}
