import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

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
