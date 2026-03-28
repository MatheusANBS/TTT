import type { Metadata } from 'next';
import { Inter, Orbitron } from 'next/font/google';
import './globals.css';

const inter = Inter({
  variable: '--font-inter',
  subsets: ['latin'],
});

const orbitron = Orbitron({
  variable: '--font-orbitron',
  subsets: ['latin'],
});

export const metadata: Metadata = {
  title: 'TTT – Advanced Memory Tool',
  description:
    'Precision Memory Control. Total Power. Scan, edit, debug and dominate any process with precision.',
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body
        className={`${inter.variable} ${orbitron.variable} antialiased selection:bg-primary/30 selection:text-primary`}
      >
        {children}
      </body>
    </html>
  );
}
