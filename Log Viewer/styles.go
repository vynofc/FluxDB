package main

import (
	"github.com/charmbracelet/lipgloss"
)

var (
	primaryColor  = lipgloss.Color("#6C5CE7")
	successColor  = lipgloss.Color("#00B894")
	errorColor    = lipgloss.Color("#FF7675")
	textColor     = lipgloss.Color("#E0E0E0")
	dimColor      = lipgloss.Color("#636E72")
	bgColor       = lipgloss.Color("#1A1A2E")

	titleStyle = lipgloss.NewStyle().
			Foreground(primaryColor).
			Bold(true)

	dimStyle = lipgloss.NewStyle().
			Foreground(dimColor)

	helpStyle = lipgloss.NewStyle().
			Foreground(dimColor)

	searchStyle = lipgloss.NewStyle().
			Foreground(primaryColor).
			Bold(true)

	searchMatchStyle = lipgloss.NewStyle().
			Foreground(primaryColor).
			Background(lipgloss.Color("#2D2D5E"))

	searchCurrentMatchStyle = lipgloss.NewStyle().
			Foreground(lipgloss.Color("#FFFFFF")).
			Background(primaryColor)

	searchInputStyle = lipgloss.NewStyle().
			Foreground(primaryColor)
)