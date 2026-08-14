namespace Mamao.SharedKernel;

/// <summary>
/// Marca um identificador tipado (EmployeeId, DepartmentId, PositionId…).
///
/// Existe por um motivo so, e vale registrar: sem ele, o gerador de OpenAPI olha para um
/// <c>readonly record struct</c> com JsonConverter proprio, nao consegue inferir nada e
/// emite um schema VAZIO — que o openapi-typescript traduz para <c>unknown</c>. O
/// contrato passa a dizer "id: unknown", o TypeScript perde a capacidade de checar
/// qualquer coisa que envolva id, e ninguem percebe porque o build continua verde.
///
/// Com a marca, o transformer em Mamao.Api declara todos eles como string/uuid de uma vez,
/// e todo id novo entra certo sem ninguem lembrar de nada.
/// </summary>
public interface IStronglyTypedId;
