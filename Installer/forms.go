package main

import (
	"fmt"

	"github.com/charmbracelet/huh"
)

func buildVersionForm(releases []releaseInfo, selected *string) *huh.Form {
	options := make([]huh.Option[string], len(releases))
	for i, r := range releases {
		label := r.tag
		if i == 0 {
			label = fmt.Sprintf("%s  (neueste)", r.tag)
		}
		if r.prerelease {
			label = fmt.Sprintf("%s  ⚠ Beta", label)
		}
		options[i] = huh.NewOption(label, r.tag)
	}

	form := huh.NewForm(
		huh.NewGroup(
			huh.NewSelect[string]().
				Title("Welche Version soll installiert werden?").
				Description("Waehle eine Version aus der Liste.").
				Options(options...).
				Value(selected),
		),
	)

	return form
}

func buildShortcutForm(createShortcut *bool) *huh.Form {
	form := huh.NewForm(
		huh.NewGroup(
			huh.NewConfirm().
				Title("Verknuepfungen erstellen?").
				Description("Sollen Desktop- und Startmenue-Verknuepfungen erstellt werden?").
				Affirmative("Ja, erstellen!").
				Negative("Nein, danke").
				Value(createShortcut),
		),
	)

	return form
}