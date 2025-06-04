// Stripe.js is loaded on demand and only when the backend is running with a test key.
declare global {
  interface Window {
    Stripe?: (key: string) => StripeLike;
  }
}

export interface StripeLike {
  elements(): { create(type: 'card'): { mount(el: HTMLElement): void } };
  confirmCardPayment(secret: string, data: { payment_method: { card: unknown } }): Promise<{ error?: { message: string } }>;
}

let loading: Promise<void> | null = null;

export function loadStripe(publishableKey: string): Promise<StripeLike> {
  loading ??= new Promise<void>((resolve, reject) => {
    const script = document.createElement('script');
    script.src = 'https://js.stripe.com/v3/';
    script.onload = () => resolve();
    script.onerror = () => reject(new Error('Stripe.js failed to load'));
    document.head.appendChild(script);
  });
  return loading.then(() => window.Stripe!(publishableKey));
}
