import { HttpErrorResponse } from '@angular/common/http';

/**
 * Flattens an ASP.NET Core `ValidationProblemDetails.errors` dict (field/
 * code -> message[]) into a plain list of messages. The dict's keys aren't
 * always form field names — e.g. register's 400 uses Identity's error
 * codes ("PasswordTooShort"), and the booking endpoints use their own
 * field names ("date", "timeSlotId") — so callers show these as a generic
 * list rather than trying to attach them to a specific control.
 */
export function extractValidationErrors(error: HttpErrorResponse): string[] {
  const errors = (error.error as { errors?: Record<string, string[]> } | null)?.errors;
  if (!errors) {
    return [];
  }

  return Object.values(errors).flat();
}
