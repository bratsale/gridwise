// The class of a list row: its status class, plus "selected" for the row whose detail (history) is open.
export const rowClass = (status: string, selected: boolean) => [status, selected ? 'selected' : ''].filter(Boolean).join(' ') || undefined
