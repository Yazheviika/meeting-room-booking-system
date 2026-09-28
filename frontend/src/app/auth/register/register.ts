import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { AuthService } from '../auth.service';
import { PasswordRuleErrors, passwordRulesValidator } from '../password-rules.validator';
import { extractValidationErrors } from '../../shared/validation-problem';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule],
  templateUrl: './register.html',
  styleUrl: './register.scss',
})
export class Register {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, passwordRulesValidator]],
  });

  protected readonly submitting = signal(false);
  protected readonly serverErrors = signal<string[]>([]);

  protected get passwordRuleErrors(): PasswordRuleErrors {
    return (this.form.controls.password.errors?.['passwordRules'] as PasswordRuleErrors | undefined) ?? {};
  }

  protected onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.serverErrors.set([]);

    const { email, password } = this.form.getRawValue();
    this.authService.register(email, password).subscribe({
      next: () => {
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/rooms';
        this.router.navigateByUrl(returnUrl);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        if (error instanceof HttpErrorResponse && error.status === 400) {
          const messages = extractValidationErrors(error);
          this.serverErrors.set(messages.length > 0 ? messages : ['Registration failed. Please try again.']);
        } else {
          this.serverErrors.set(['Something went wrong. Please try again.']);
        }
      },
    });
  }
}
