package main

import (
	"github.com/charmbracelet/lipgloss"
)

var (
	primaryColor = lipgloss.Color("#6C5CE7")
	dimColor     = lipgloss.Color("#636E72")

	appStyle = lipgloss.NewStyle()

	titleStyle = lipgloss.NewStyle().
			Foreground(primaryColor).
			Bold(true)

	dimStyle = lipgloss.NewStyle().
			Foreground(dimColor)

	helpStyle = lipgloss.NewStyle().
			Foreground(dimColor)
)