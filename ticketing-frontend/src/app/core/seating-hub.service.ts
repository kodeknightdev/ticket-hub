import { Injectable, NgZone, inject } from '@angular/core';
import { HttpTransportType, HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { SeatState } from './models';

export interface SeatsChanged {
  eventId: string;
  seatIds: string[];
  state: SeatState;
}

export interface PriceChanged {
  eventId: string;
  tierId: string;
  price: number;
}

@Injectable({ providedIn: 'root' })
export class SeatingHubService {
  private readonly zone = inject(NgZone);
  private connection: HubConnection | null = null;
  private joined = new Set<string>();

  readonly seats$ = new Subject<SeatsChanged>();
  readonly prices$ = new Subject<PriceChanged>();

  async join(eventId: string): Promise<void> {
    const connection = await this.connect();
    if (!this.joined.has(eventId)) {
      await connection.invoke('JoinEvent', eventId);
      this.joined.add(eventId);
    }
  }

  async leave(eventId: string): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected && this.joined.delete(eventId)) {
      await this.connection.invoke('LeaveEvent', eventId);
    }
  }

  private async connect(): Promise<HubConnection> {
    if (this.connection) {
      return this.connection;
    }
    // WebSockets only, no negotiate round trip, so any seating replica can take the connection.
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/seating', { transport: HttpTransportType.WebSockets, skipNegotiation: true })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('SeatsChanged', (change: SeatsChanged) => this.zone.run(() => this.seats$.next(change)));
    connection.on('PriceChanged', (change: PriceChanged) => this.zone.run(() => this.prices$.next(change)));
    connection.onreconnected(async () => {
      for (const eventId of this.joined) {
        await connection.invoke('JoinEvent', eventId);
      }
    });

    this.connection = connection;
    await connection.start();
    return connection;
  }
}
