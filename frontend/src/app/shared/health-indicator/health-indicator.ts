import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

type HealthStatus = 'checking' | 'healthy' | 'unreachable';

/** Small toolbar badge proving the backend is reachable — see home.ts (PR 1) for the original, full-page version this was extracted from. */
@Component({
  selector: 'app-health-indicator',
  imports: [],
  templateUrl: './health-indicator.html',
  styleUrl: './health-indicator.scss',
})
export class HealthIndicator implements OnInit {
  private readonly http = inject(HttpClient);

  protected readonly status = signal<HealthStatus>('checking');

  ngOnInit(): void {
    this.http.get(`${environment.apiBaseUrl}/health`).subscribe({
      next: () => this.status.set('healthy'),
      error: () => this.status.set('unreachable'),
    });
  }
}
