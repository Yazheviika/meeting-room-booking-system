import { HttpErrorResponse } from '@angular/common/http';
import { extractValidationErrors } from './validation-problem';

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
