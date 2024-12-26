using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IoTGateway.DataAccess.Migrations
{
    public partial class morepublishmethods : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GatewayToken",
                table: "SystemConfig",
                type: "TEXT",
                nullable: true,
                comment: "认证令牌");

            migrationBuilder.AddColumn<string>(
                name: "HttpEndpoint",
                table: "SystemConfig",
                type: "TEXT",
                nullable: true,
                comment: "HTTP/WebSocket端点");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GatewayToken",
                table: "SystemConfig");

            migrationBuilder.DropColumn(
                name: "HttpEndpoint",
                table: "SystemConfig");
        }
    }
}
