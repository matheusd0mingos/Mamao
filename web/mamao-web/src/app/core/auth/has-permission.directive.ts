import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';
import { SessionService } from './session.service';

/**
 * `<button *mamaoHasPermission="'people.write'">` — esconde o que a pessoa nao pode fazer.
 * Isto e conforto, nao seguranca: quem protege e a policy no endpoint.
 */
@Directive({ selector: '[mamaoHasPermission]' })
export class HasPermissionDirective {
  private readonly session = inject(SessionService);
  private readonly template = inject(TemplateRef<unknown>);
  private readonly container = inject(ViewContainerRef);

  readonly mamaoHasPermission = input.required<string>();

  constructor() {
    effect(() => {
      const permitido = this.session.has(this.mamaoHasPermission());
      this.container.clear();

      if (permitido) {
        this.container.createEmbeddedView(this.template);
      }
    });
  }
}
