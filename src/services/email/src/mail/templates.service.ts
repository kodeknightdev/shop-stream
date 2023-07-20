import { Inject, Injectable, NotFoundException, OnModuleInit, Optional } from '@nestjs/common';
import Handlebars from 'handlebars';
import { readFileSync, readdirSync } from 'fs';
import { join } from 'path';

export const TEMPLATES_DIR = Symbol('TEMPLATES_DIR');
export const TEMPLATE_NAMES = ['order-placed', 'order-confirmed', 'order-cancelled', 'payment-failed'] as const;
export type TemplateName = (typeof TEMPLATE_NAMES)[number];

@Injectable()
export class TemplatesService implements OnModuleInit {
  private readonly handlebars = Handlebars.create();
  private readonly compiled = new Map<string, Handlebars.TemplateDelegate>();
  private layout!: Handlebars.TemplateDelegate;

  private readonly dir: string;

  constructor(@Optional() @Inject(TEMPLATES_DIR) dir?: string) {
    this.dir = dir ?? join(__dirname, '..', '..', 'templates');
  }

  onModuleInit() {
    this.handlebars.registerHelper('money', (value: unknown, currency: unknown) => {
      const amount = Number(value ?? 0);
      const code = typeof currency === 'string' && currency.length === 3 ? currency : 'USD';
      return new Intl.NumberFormat('en-US', { style: 'currency', currency: code }).format(amount);
    });

    for (const file of readdirSync(this.dir).filter((f) => f.endsWith('.hbs'))) {
      const name = file.replace(/\.hbs$/, '');
      const template = this.handlebars.compile(readFileSync(join(this.dir, file), 'utf8'));
      if (name === 'layout') this.layout = template;
      else this.compiled.set(name, template);
    }
  }

  names(): string[] {
    return [...this.compiled.keys()].sort();
  }

  render(name: string, subject: string, data: Record<string, unknown>): string {
    const template = this.compiled.get(name);
    if (!template) throw new NotFoundException(`Unknown template '${name}'`);
    const body = template(data);
    return this.layout({ ...data, subject, body });
  }
}
