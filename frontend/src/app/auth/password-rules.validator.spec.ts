import { FormControl } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { extractValidationErrors, passwordRulesValidator } from './password-rules.validator';

describe('passwordRulesValidator', () => {
  it('passes a password that satisfies every rule', () => {
    const control = new FormControl('Passw0rd!');
    expect(passwordRulesValidator(control)).toBeNull();
  });

  it('flags a password that is too short', () => {
    const control = new FormControl('Pw0!');
    expect(passwordRulesValidator(control)?.['passwordRules']).toMatchObject({ minLength: true });
  });

  it('flags a password missing a digit', () => {
    const control = new FormControl('Password!');
    expect(passwordRulesValidator(control)?.['passwordRules']).toMatchObject({ requireDigit: true });
  });

  it('flags a password missing a lowercase letter', () => {
    const control = new FormControl('PASSW0RD!');
    expect(passwordRulesValidator(control)?.['passwordRules']).toMatchObject({ requireLowercase: true });
  });

  it('flags a password missing an uppercase letter', () => {
    const control = new FormControl('passw0rd!');
    expect(passwordRulesValidator(control)?.['passwordRules']).toMatchObject({ requireUppercase: true });
  });

  it('flags a password missing a non-alphanumeric character', () => {
    const control = new FormControl('Passw0rd');
    expect(passwordRulesValidator(control)?.['passwordRules']).toMatchObject({ requireNonAlphanumeric: true });
  });

  it('treats an empty value as failing every rule', () => {
    const control = new FormControl('');
    expect(passwordRulesValidator(control)?.['passwordRules']).toMatchObject({
      minLength: true,
      requireDigit: true,
      requireLowercase: true,
      requireUppercase: true,
      requireNonAlphanumeric: true,
    });
  });
});

describe('extractValidationErrors', () => {
  it('flattens all message arrays from the errors dict', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: {
        errors: {
          PasswordTooShort: ['Passwords must be at least 6 characters.'],
          PasswordRequiresNonAlphanumeric: ['Passwords must have at least one non alphanumeric character.'],
        },
      },
    });

    expect(extractValidationErrors(error)).toEqual([
      'Passwords must be at least 6 characters.',
      'Passwords must have at least one non alphanumeric character.',
    ]);
  });

  it('returns an empty list when there is no errors dict', () => {
    const error = new HttpErrorResponse({ status: 401, error: null });
    expect(extractValidationErrors(error)).toEqual([]);
  });
});
