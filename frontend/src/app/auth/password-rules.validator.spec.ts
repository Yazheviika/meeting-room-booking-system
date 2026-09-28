import { FormControl } from '@angular/forms';
import { passwordRulesValidator } from './password-rules.validator';

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
