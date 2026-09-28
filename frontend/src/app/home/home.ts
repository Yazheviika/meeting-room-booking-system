import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';

type HealthStatus = 'checking' | 'healthy' | 'unreachable';

@Component({
  selector: 'app-home',
  imports: [],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home implements OnInit {
  private readonly http = inject(HttpClient);

  protected readonly status = signal<HealthStatus>('checking');

  ngOnInit(): void {
    this.http.get(`${environment.apiBaseUrl}/health`).subscribe({
      next: () => this.status.set('healthy'),
      error: () => this.status.set('unreachable'),
    });
  }
}
