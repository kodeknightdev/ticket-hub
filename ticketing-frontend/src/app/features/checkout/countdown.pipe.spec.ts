import { CountdownPipe } from './countdown.pipe';

describe('CountdownPipe', () => {
  const pipe = new CountdownPipe();

  it('formats minutes and seconds', () => {
    expect(pipe.transform(600)).toBe('10:00');
    expect(pipe.transform(65)).toBe('1:05');
    expect(pipe.transform(0)).toBe('0:00');
  });

  it('never goes negative', () => {
    expect(pipe.transform(-5)).toBe('0:00');
    expect(pipe.transform(null)).toBe('0:00');
  });
});
