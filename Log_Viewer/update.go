package main

import (
	"bufio"
	"log"
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
		m.err = msg.error
		m.ready = true
		return m, nil
	}

	return m, nil
}

func (m *model) handleKey(msg tea.KeyMsg) (tea.Model, tea.Cmd) {
	switch msg.String() {
	case "ctrl+c", "esc", "q":
		return m, tea.Quit
	case "r":
		m.err = nil
		m.ready = false
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

	if _, err := f.Seek(m.lastSize, 0); err != nil {
		return
	}
	scanner := bufio.NewScanner(f)
	scanner.Buffer(make([]byte, 1024*1024), 1024*1024)
	bytesRead := int64(0)
	for scanner.Scan() {
		raw := scanner.Text()
		m.lines = append(m.lines, raw)
		m.styledLines = append(m.styledLines, styleLogLine(raw))
		bytesRead += int64(len(raw)) + 1 // +1 for the newline consumed by Scan
	}
	if err := scanner.Err(); err != nil {
		log.Printf("Fehler beim Lesen der Logdatei: %v", err)
	}
	m.lastSize += bytesRead
	m.viewport.SetContent(strings.Join(m.styledLines, "\n"))
	m.viewport.GotoBottom()
}
