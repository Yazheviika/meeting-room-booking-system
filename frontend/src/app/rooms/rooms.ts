import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { RoomsService, Room } from './rooms.service';

@Component({
  selector: 'app-rooms',
  imports: [RouterLink, MatCardModule],
  templateUrl: './rooms.html',
  styleUrl: './rooms.scss',
})
export class Rooms implements OnInit {
  private readonly roomsService = inject(RoomsService);

  protected readonly rooms = signal<Room[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.roomsService.getRooms().subscribe({
      next: (rooms) => {
        this.rooms.set(rooms);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load rooms.');
        this.loading.set(false);
      },
    });
  }
}
