package main

import (
	"github.com/charmbracelet/bubbles/progress"
	"github.com/charmbracelet/bubbles/spinner"
	tea "github.com/charmbracelet/bubbletea"
	"github.com/charmbracelet/lipgloss"
)

type state int

const (
	stateFetchingTag state = iota
	stateDownloading
	stateExtracting
	stateDone
	stateError
)

type tagFetchedMsg struct {
	tag string
}

type downloadProgressMsg struct {
	percent float64
}

type downloadCompleteMsg struct {
	path string
}

type extractCompleteMsg struct{}

type errMsg struct {
	err error
}

type model struct {
	state       state
	tag         string
	progress    float64
	err         error
	zipPath     string
	spinner     spinner.Model
	progressBar progress.Model
	width       int
	height      int
	silent      bool
	customTag   string
	customPath  string
}

func initialModel(customTag, customPath string, silent bool) model {
	s := spinner.New()
	s.Spinner = spinner.Dot
	s.Style = lipgloss.NewStyle().Foreground(lipgloss.Color("#6C5CE7"))

	pb := progress.New(
		progress.WithDefaultGradient(),
		progress.WithWidth(40),
		progress.WithoutPercentage(),
	)

	return model{
		state:       stateFetchingTag,
		spinner:     s,
		progressBar: pb,
		silent:      silent,
		customTag:   customTag,
		customPath:  customPath,
	}
}

func (m model) Init() tea.Cmd {
	return tea.Batch(
		m.spinner.Tick,
		fetchTagCmd(m.customTag),
	)
}