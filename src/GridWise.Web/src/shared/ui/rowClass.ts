// The class of a list row: its status class, plus "selected" for the row whose detail (history) is open and "flash" for the row
// that was just edited, retired or created (highlighted briefly when the form closes).
export const rowClass = (status: string, selected: boolean, flashing = false) =>
  [status, selected ? 'selected' : '', flashing ? 'flash' : ''].filter(Boolean).join(' ') || undefined
