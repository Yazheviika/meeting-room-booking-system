import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';

/**
 * Mirrors ASP.NET Core Identity's untouched default password options (no
 * IdentityOptions.Password override in Program.cs): minimum length 6, and
 * at least one digit, one lowercase letter, one uppercase letter, and one
 * non-alphanumeric character. Returns which specific rule(s) failed so the
 * register form can show a live checklist instead of one opaque error.
 */
export interface PasswordRuleErrors {
  minLength?: true;
  requireDigit?: true;
  requireLowercase?: true;
  requireUppercase?: true;
  requireNonAlphanumeric?: true;
}

export const passwordRulesValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value = (control.value as string | null) ?? '';

  const errors: PasswordRuleErrors = {};
  if (value.length < 6) {
    errors.minLength = true;
  }
  if (!/\d/.test(value)) {
    errors.requireDigit = true;
  }
  if (!/[a-z]/.test(value)) {
    errors.requireLowercase = true;
  }
  if (!/[A-Z]/.test(value)) {
    errors.requireUppercase = true;
  }
  if (!/[^a-zA-Z0-9]/.test(value)) {
    errors.requireNonAlphanumeric = true;
  }

  return Object.keys(errors).length > 0 ? { passwordRules: errors } : null;
};

/**
 * Register's 400 response is a ValidationProblemDetails whose `errors`
 * dict is keyed by ASP.NET Identity's error codes (e.g. "PasswordTooShort",
 * "DuplicateUserName"), not by form field names — so there's no specific
 * control to attach these to. Flattened into a plain list instead.
 */
export function extractValidationErrors(error: HttpErrorResponse): string[] {
  const errors = (error.error as { errors?: Record<string, string[]> } | null)?.errors;
  if (!errors) {
    return [];
  }

  return Object.values(errors).flat();
}
