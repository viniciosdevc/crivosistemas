/** Shared look of text-like controls (input, select, textarea). */
export function fieldClasses(invalid: boolean, extra = '') {
  return [
    'w-full rounded-[var(--radius-field)] border bg-surface px-4 text-[15px] text-ink transition-colors',
    'hover:border-line-strong focus:outline-none focus:ring-4',
    invalid ? 'border-danger focus:border-danger focus:ring-danger-soft' : 'border-line focus:border-sel-ring focus:ring-brand-soft',
    'disabled:cursor-not-allowed disabled:bg-surface-2 disabled:text-muted',
    extra,
  ].join(' ')
}
