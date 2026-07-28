package main

import (
	"fmt"

	"github.com/charmbracelet/huh"
)

func buildVersionForm(releases []string) *huh.Form {
	options := make([]huh.Option[string], len(releases))
	for i, r := range releases {
		label := r
		if i == 0 {
			label = fmt.Sprintf("%s  (neueste)", r)
		}
		options[i] = huh.NewOption(label, r)
	}

	var selected string
	form := huh.NewForm(
		huh.NewGroup(
			huh.NewSelect[string]().
				Title("Welche Version soll installiert werden?").
				Description("Waehle eine Version aus der Liste.").
				Options(options...).
				Value(&selected),
		),
	)

	return form
}

func buildShortcutForm() *huh.Form {
	var createShortcut bool
	form := huh.NewForm(
		huh.NewGroup(
			huh.NewConfirm().
				Title("Desktop-Verknuepfung erstellen?").
				Description("Soll eine Verknuepfung auf dem Desktop erstellt werden?").
				Affirmative("Ja, erstellen!").
				Negative("Nein, danke").
				Value(&createShortcut),
		),
	)

	return form
}