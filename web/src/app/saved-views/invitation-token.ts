/** The shape of an invitation token the API makes: 43 URL-safe characters. Checked before anything is sent, so a pasted or mangled link costs no request. */
export const InvitationTokenShape = {
  length: 43,
  isWellFormed(value: string | null | undefined): value is string {
    return typeof value === 'string' && value.length === 43 && /^[A-Za-z0-9_-]+$/u.test(value);
  },
} as const;
