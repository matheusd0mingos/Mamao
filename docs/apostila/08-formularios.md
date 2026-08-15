# Capítulo 8 — Formulários

> **Objetivo:** construir formulários com validação e, principalmente, fazer o erro do
> servidor aparecer no campo certo sem escrever código para cada tela.

---

## 8.1 Duas famílias

O Angular tem duas abordagens.

**Template-driven** — o formulário vive no HTML, com `ngModel`:

```html
<input [(ngModel)]="nome" required>
```

**Reactive Forms** — o formulário é um objeto no TypeScript:

```typescript
readonly form = new FormGroup({
  fullName: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
});
```

O Mamão usa **Reactive** em quase tudo. Motivos: é tipado, é testável sem renderizar a
tela, e a validação fica em um lugar só.

Template-driven aparece em casos pontuais — e por um motivo específico que vale contar
(seção 8.6).

## 8.2 O básico do Reactive

```typescript
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

@Component({
  imports: [ReactiveFormsModule],
  template: `
    <form [formGroup]="form" (ngSubmit)="salvar()">
      <label>
        Nome
        <input formControlName="fullName">
      </label>

      @if (form.controls.fullName.touched && form.controls.fullName.invalid) {
        <small class="erro">Informe o nome.</small>
      }

      <button [disabled]="form.invalid || salvando()">Salvar</button>
    </form>
  `,
})
export class Formulario {
  readonly salvando = signal(false);

  readonly form = new FormGroup({
    fullName: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.email] }),
  });

  async salvar(): Promise<void> {
    if (this.form.invalid) return;
    this.salvando.set(true);
    try {
      await this.api.create(this.form.getRawValue());
    } finally {
      this.salvando.set(false);
    }
  }
}
```

### `nonNullable: true`

Sem isso, `FormControl('')` tem tipo `string | null`, porque `reset()` volta para `null`.
Com `nonNullable`, `reset()` volta para `''` e o tipo é só `string`. Menos verificação de
nulo espalhada.

### Estados de um campo

| Estado | Significado |
|---|---|
| `pristine` / `dirty` | o usuário alterou? |
| `touched` / `untouched` | o usuário entrou e saiu do campo? |
| `valid` / `invalid` | passou nas validações? |

Mostrar erro só quando `touched && invalid` é o que evita a tela nascer vermelha antes de a
pessoa digitar qualquer coisa.

## 8.3 Validadores

```typescript
Validators.required
Validators.email
Validators.minLength(8)
Validators.max(365)
```

Validador próprio é uma função:

```typescript
function naoPodeSerFuturo(control: AbstractControl): ValidationErrors | null {
  const data = new Date(control.value);
  return data > new Date() ? { futuro: true } : null;
}
```

Devolve `null` quando está tudo bem — o que é contraintuitivo, mas é a convenção: "não há
erros".

⚠️ **Regra que vale para sempre:** validação no frontend é **conveniência**. Feedback
imediato, sem ida ao servidor. Ela **não** protege nada — quem valida de verdade é o
backend. No Mamão, com FluentValidation:

```csharp
public sealed class CreateEmployeeRequestValidator : AbstractValidator<CreateEmployeeRequest>
{
    public CreateEmployeeRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200)
            .WithMessage("Informe o nome do funcionario.");
        RuleFor(x => x.PositionId).Must(id => id.Value != Guid.Empty)
            .WithMessage("Informe o cargo do funcionario.");
    }
}
```

## 8.4 O erro do servidor no campo certo

Aqui está a parte boa, e é uma integração de verdade entre .NET e Angular.

O problema: o backend rejeitou a matrícula por já existir. Como fazer essa mensagem
aparecer **embaixo do campo matrícula**, e não num alerta genérico no topo?

### No .NET: o formato do erro

A API devolve **ProblemDetails** (padrão RFC 7807) com um extra:

```json
{
  "title": "Requisição inválida",
  "status": 400,
  "code": "employee.duplicate_code",
  "traceId": "00-18c4d7f6…",
  "fieldErrors": {
    "code": ["Já existe um funcionário com esta matrícula."]
  }
}
```

`fieldErrors` mapeia **nome do campo** → **lista de mensagens**. Os nomes batem com os do
formulário porque ambos vêm do mesmo DTO.

### No Angular: o tipo

```typescript
// src/app/core/http/api.types.ts
/**
 * Forma unica de erro vinda do backend. O `code` e estavel e serve para o frontend
 * decidir; `fieldErrors` alimenta o formulario direto, sem codigo por tela.
 */
export interface ApiProblem {
  title: string;
  detail: string;
  status: number;
  code?: string;
  traceId?: string;
  fieldErrors?: Record<string, string[]>;
}
```

### Aplicando no formulário

```typescript
catch (erro) {
  const problema = erro as ApiProblem;

  for (const [campo, mensagens] of Object.entries(problema.fieldErrors ?? {})) {
    this.form.get(campo)?.setErrors({ servidor: mensagens[0] });
  }
}
```

E no template:

```html
@if (form.controls.code.errors?.['servidor']; as msg) {
  <small class="erro">{{ msg }}</small>
}
```

**Nenhum `if` por tipo de erro. Nenhum código específico de tela.** O contrato carrega o
nome do campo, e o laço distribui. Adicionar uma regra nova no C# faz a mensagem aparecer
no lugar certo sem tocar no frontend.

É por isso que existe um teste de integração no Mamão guardando exatamente esse formato:

```csharp
[Fact]
public async Task Validacao_devolve_erro_por_campo_no_formato_que_o_formulario_consome()
{
    // …
    corpo.ShouldContain("fieldErrors");
    corpo.ShouldContain("fullName");
    corpo.ShouldContain("positionId");
}
```

Se alguém mudar o formato do erro, esse teste quebra — antes de a tela quebrar em produção.

## 8.5 Envio de arquivo

Upload não é JSON:

```typescript
/**
 * FormData de proposito: o arquivo sobe como multipart, sem base64. O nome do campo tem
 * que ser "arquivo" — e o nome do parametro IFormFile no endpoint.
 * Nao definimos Content-Type: o navegador precisa gerar o boundary.
 */
function corpo(arquivo: File): FormData {
  const dados = new FormData();
  dados.append('arquivo', arquivo, arquivo.name);
  return dados;
}
```

Duas armadilhas, ambas no comentário:

1. **O nome do campo tem que bater com o parâmetro no C#.** Se o endpoint é
   `IFormFile arquivo`, o `append` tem que usar `'arquivo'`. Errar aqui dá "arquivo não
   enviado" com o arquivo tendo sido enviado.
2. **Não defina `Content-Type` na mão.** O multipart precisa de um *boundary* — um
   separador aleatório entre as partes. O navegador gera; se você sobrescrever o cabeçalho,
   o boundary some e o servidor não consegue separar nada.

## 8.6 Quando o Mamão usa `ngModel` — e por quê

Um bug real que ensina bastante.

A tela do chefe de setor tinha um `<select>` de setores preenchido por uma segunda
requisição. O código era:

```html
<select [value]="chefe.setorId">
  @for (s of setores(); track s.id) { <option [value]="s.id">{{ s.nome }}</option> }
</select>
```

O `<select>` mostrava "Ninguém" mesmo para setores que tinham chefe.

**Causa:** `[value]` num `<select>` é aplicado no momento em que o elemento é criado. Nesse
instante, as `<option>` ainda não existem — elas dependem de uma requisição que ainda não
voltou. Definir o valor de um `<select>` que não tem a opção correspondente **não faz
nada**, silenciosamente. Quando as opções chegam, ninguém reaplica o valor.

**Correção:**

```html
<select [ngModel]="chefe.setorId" [ngModelOptions]="{ standalone: true }">
```

`ngModel` observa o valor **e** a chegada das opções, e reaplica quando ambas existem.
`standalone: true` diz que aquele controle não pertence a um `FormGroup` — necessário para
o Angular não reclamar.

**Lição geral:** todo binding que depende de dado assíncrono precisa se reconciliar quando
o dado chega. É a mesma classe de bug do `track $index`.

---

## Para fixar

1. **Por que `nonNullable: true`?**
   <details><summary>Resposta</summary>
   Sem ele, o tipo do controle é `string | null` porque `reset()` volta a `null`, e você
   passa a tratar nulo em todo lugar. Com ele, `reset()` volta à string vazia e o tipo é
   `string`.
   </details>

2. **O que `fieldErrors` resolve que uma mensagem única não resolve?**
   <details><summary>Resposta</summary>
   Coloca a mensagem embaixo do campo que a causou, sem código por tela. Um alerta genérico
   no topo obriga o usuário a descobrir sozinho qual campo está errado.
   </details>

3. **Por que não definir `Content-Type: multipart/form-data` manualmente?**
   <details><summary>Resposta</summary>
   Porque o cabeçalho precisa incluir o boundary gerado pelo navegador. Definindo à mão,
   você o omite e o servidor não consegue separar as partes.
   </details>

## Laboratório

1. Formulário com nome (obrigatório, máx. 200) e e-mail (formato válido). Erro só depois de
   `touched`.
2. Botão desabilitado com `form.invalid || salvando()`. Simule uma espera de 2 segundos e
   observe o botão.
3. **Simule o erro do servidor:** no `catch`, injete manualmente
   `{ fieldErrors: { email: ['Este e-mail não está disponível.'] } }` e faça a mensagem
   aparecer no campo.
4. **Reproduza o bug do `<select>`:** popule as opções com `setTimeout(..., 2000)` e use
   `[value]`. Veja o valor não aplicar. Troque por `[ngModel]` e veja funcionar.

---

**Anterior:** [Capítulo 7](07-rotas-e-guardas.md) ·
**Próximo:** [Capítulo 9 — O contrato: OpenAPI como fonte da verdade](09-o-contrato-openapi.md)
