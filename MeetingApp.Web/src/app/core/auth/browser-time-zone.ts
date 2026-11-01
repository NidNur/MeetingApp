export function getBrowserTimeZone(): string | null {
  try {
    const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    return typeof timeZone === 'string' && timeZone.trim() ? timeZone.trim() : null;
  } catch {
    return null;
  }
}
